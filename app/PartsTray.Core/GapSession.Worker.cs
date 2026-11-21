// 応答の照合・再送と、許可した HID 書き込みだけの提出を管理します。
// 送信の許可条件や再送規則を変えるときに開きます。
namespace PartsTray.Core;

public sealed partial class GapSession
{
    private void Match(HidReport r)
    {
        var p = pending;
        if (p is null || r.Device != p.Command.Device || r.Feature != p.Command.Feature ||
            r.Function != p.Command.Function || r.Swid != 5) return;
        if (r.Kind is ReportKind.Error10 or ReportKind.Error20)
        {
            log.Write($"応答 {p.Command.Name}: {r.Kind} code={r.Error}");
            if (r.Kind == ReportKind.Error20 && r.Error == 8) { p.Busy = true; p.Deadline = clock.Now + TimeSpan.FromMilliseconds(50); }
            else Fail(p);
            return;
        }
        if (r.Kind != ReportKind.Response) return;
        // CID / sensor echoes prevent unrelated or late responses advancing a different command.
        if (r.Device == device && features.TryGetValue(0x1B04, out var cidIndex) && r.Feature == cidIndex &&
            r.Function is 2 or 3 && !r.Parameters.AsSpan(0, 2).SequenceEqual(p.Command.Parameters.AsSpan(0, 2))) return;
        if (r.Device == device && features.TryGetValue(0x2201, out var dpiIndex) && r.Feature == dpiIndex && r.Function == 3 &&
            !r.Parameters.AsSpan(0, 3).SequenceEqual(p.Command.Parameters.AsSpan(0, 3))) return;
        pending = null;
        log.Write($"応答 {p.Command.Name}: OK {Convert.ToHexString(r.Parameters)}");
        if (p.Generation == generation && p.Command.Allowed())
        {
            try { p.Command.Success(r); }
            catch { blocked = true; throw; }
        }
    }
    private void Fail(Pending p)
    {
        pending = null;
        if (device == 0 && probe?.Device == p.Command.Device)
        {
            log.Write($"起動照会中断 dev={probe.Device}: 次の番号へ進む");
            probe = null;
            return;
        }
        if (p.Command.Device == device && !p.Command.Cleanup) blocked = true;
        log.Write($"中断 {p.Command.Name}: 未適用分は次の外部通知/応答まで待機");
    }

    /// <summary>Worker only. Tests call explicitly; no timers or threads in Core.</summary>
    public void Pump()
    {
        lock (gate)
        {
            try
            {
                if (bridgeActive && state == SessionState.Retreated &&
                    clock.Now >= retreatAt + TimeSpan.FromSeconds(sessionSettings.CleanupDelaySeconds))
                    StopBridge($"引き継ぎから {sessionSettings.CleanupDelaySeconds} 秒");
                while (nativeWheel.TryDequeue(out int nativeDelta))
                {
                    if (paused) continue;
                    int delta = nativeScaler.Convert(nativeDelta, settings.ThumbWheel.SpeedPercent, settings.ThumbWheel.DirectionSign);
                    if (delta == 0) continue;
                    if (bridgeActive)
                    {
                        bridgeReceived++;
                        bridgeReceivedTotal += nativeDelta;
                        if (bridgeExamples.Count < 5) bridgeExamples.Add(nativeDelta);
                    }
                    if (settings.ThumbWheel.SmoothMs == 0) SendNativeWheel(delta);
                    else smoother.Add(delta, clock.Now);
                }
                if (!paused)
                {
                    int slice = smoother.Take(clock.Now);
                    if (slice != 0) SendNativeWheel(slice);
                }
                // Bounded for synchronous fake replies; real HID normally stops at the first pending request.
                for (int n = 0; n < 128; n++)
                {
                    if (paused && !ListenWhilePaused) return;
                    if (pending is { } p)
                    {
                        if (!p.Command.Allowed() || p.Generation != generation) { pending = null; continue; }
                        if (clock.Now < p.Deadline) return;
                        if (p.Retries >= 3) { Fail(p); return; }
                        p.Retries++; p.Busy = false; p.Deadline = clock.Now + TimeSpan.FromSeconds(1);
                        Submit(p); if (pending != null) return; continue;
                    }
                    var command = NextCommand();
                    if (command == null) return;
                    if (!command.Allowed()) continue;
                    var next = new Pending(command, generation, clock.Now + TimeSpan.FromSeconds(1));
                    pending = next; Submit(next);
                    if (pending != null) return;
                }
            }
            catch (Exception ex)
            {
                if (pending is { } p) Fail(p);
                else blocked = true;
                log.Write($"ワーカーエラー: {ex}");
            }
        }
    }
    private void SendNativeWheel(int delta)
    {
        try
        {
            input.HorizontalWheel(delta);
            if (bridgeActive) { bridgeSent++; bridgeSentTotal += delta; }
        }
        catch (Exception ex)
        {
            if (!bridgeFailureLogged)
            {
                bridgeFailureLogged = true;
                log.Write($"サムホイール送信失敗: {ex.Message}");
            }
        }
    }
    private void Submit(Pending p)
    {
        if (!p.Command.Allowed()) { pending = null; return; }
        ValidateCommand(p.Command);
        var bytes = HidReport.Request(p.Command.Device, p.Command.Feature, p.Command.Function, p.Command.Parameters);
        log.Write($"送信 {p.Command.Name} retry={p.Retries}: {Convert.ToHexString(bytes)}");
        // Track a submitted diversion even if its ACK is lost or Options+ retreats before the ACK.
        p.Command.Submitted?.Invoke();
        transport.Send(bytes);
    }
    private void ValidateCommand(Command c)
    {
        bool ping = c.Feature == 0 && c.Function == 1 && !c.Write;
        // Review 1 permits only these identification reads on a ping-confirmed unknown device.
        bool identification = !c.Write && device == 0 && probe is { PingConfirmed: true } candidate && candidate.Device == c.Device &&
            ((c.Feature == 0 && c.Function == 0 && c.Parameters.AsSpan().SequenceEqual(new byte[] { 0, 5 })) ||
            (candidate.NameFeature != 0 && c.Feature == candidate.NameFeature &&
            ((c.Function == 0 && c.Parameters.Length == 0) ||
            (c.Function == 1 && candidate.NameLength.HasValue && c.Parameters.Length == 1 && c.Parameters[0] == candidate.Name.Count))));
        if (c.Device is < 1 or > 6 || (!ping && !identification && (c.Device != device || !identities.TryGetValue(device, out var id) || id != 0xB042)))
            throw new InvalidOperationException("未確認機器への送信を拒否");
        if (identification) return;
        if (!c.Write)
        {
            bool valid = c.Feature == 0 && c.Function is 0 or 1 ||
                features.TryGetValue(0x1B04, out var f) && c.Feature == f && c.Function == 2 ||
                features.TryGetValue(0x2150, out f) && c.Feature == f && c.Function is 0 or 1;
            if (!valid) throw new InvalidOperationException("許可リスト外の照会");
            return;
        }
        bool active = state == SessionState.Active && !c.Cleanup;
        bool cleaning = state == SessionState.Retreated && c.Cleanup && cleanupDone;
        var p = c.Parameters;
        bool allowed = features.TryGetValue(0x2201, out var index) && c.Feature == index && c.Function == 3 && active && p.Length == 3 && p[0] == 0;
        allowed |= features.TryGetValue(0x1B04, out index) && c.Feature == index && c.Function == 3 && p.Length == 5 &&
            ButtonOrder.Contains(HidReport.U16(p)) && p[0] == p[3] && p[1] == p[4] &&
            ((active && p[2] == 3) || (cleaning && p[2] == 2 && owned.Contains(HidReport.U16(p)) && CidNeedsCleanup(HidReport.U16(p))));
        allowed |= features.TryGetValue(0x2150, out index) && c.Feature == index && c.Function == 2 && p.Length == 2 &&
            ((active && p[0] == 1 && p[1] == 1) ||
            (active && p[0] == 0 && p[1] == 0 && sessionSettings.ThumbWheel.Mode == "hook") ||
            (cleaning && p[0] == 0 && p[1] == 0 && thumbOwned && !optionsThumb));
        if (!allowed) throw new InvalidOperationException("許可リスト外の書込を拒否");
    }
}
