// MX Master 4 の接続状態と肩代わり・引き継ぎを管理します。
// 有効条件や送信規則を変える場所です。純粋な状態機械としてテストします。
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace PartsTray.Core;

public enum SessionState { Disconnected, Active, Retreated, Paused }

/// <summary>
/// Receive() commits state changes synchronously on the caller's receive thread.
/// Only Pump(), called by a separate worker, submits I/O and SendInput.
/// The gate linearizes a physical write submission against retreat/disconnect.
/// No I/O completion is awaited while holding the gate.
/// </summary>
public sealed class GapSession : INativeWheelBridge
{
    public static IReadOnlyList<ushort> ButtonOrder { get; } = Array.AsReadOnly<ushort>([0x52, 0x56, 0x53, 0xC3, 0x1A0]);
    private static readonly ushort[] FeatureOrder = [0x2201, 0x1B04, 0x2150, 0x1D4B];
    private readonly object gate = new();
    private readonly IReportTransport transport;
    private readonly IInputSink input;
    private readonly IClock clock;
    private readonly ILog log;
    private readonly Func<bool> optionsRunning;
    private readonly Func<DateTime?>? optionsStartedUtc;
    private readonly Func<DateTime> utcNow;
    private readonly DateTime createdUtc;
    private DateTime? lastOptionsConfigUtc;
    private GapSettings settings;
    private GapSettings sessionSettings;
    private SessionState state;
    private byte device;
    private int generation;
    private readonly Dictionary<byte, ushort> identities = [];
    private readonly Dictionary<ushort, byte> features = [];
    private readonly Dictionary<ushort, byte> optionsFlags = [];
    private readonly List<HidReport> earlyOptions = [];
    private readonly HashSet<ushort> owned = [];
    private HashSet<ushort> pressed = [];
    private HashSet<ushort> heldButtons = [];
    private readonly Queue<(int Generation, ushort Cid, ButtonSettings? Action, int Delta)> actions = [];
    private readonly Queue<Command> cleanup = [];
    private readonly Queue<byte> probes = [];
    private readonly HashSet<(string Receiver, byte Device)> previousActive = [];
    private string receiverIdentity = "";
    private Probe? probe;
    private readonly ThumbWheelConverter wheel = new();
    private volatile bool bridgeActive;
    private readonly ConcurrentQueue<int> nativeWheel = new();
    private readonly NativeWheelScaler nativeScaler = new();
    private IWheelSmoother smoother;
    private long bridgeReceived, bridgeReceivedTotal, bridgeSent, bridgeSentTotal;
    private readonly List<int> bridgeExamples = [];
    private bool bridgeFailureLogged;
    private Pending? pending;
    private int discoveryStep, applyStep, dpi;
    private bool blocked, thumbOwned, optionsThumb, cleanupDone, paused, startupCheck, inheritedActive;
    private bool ignoredThumbLogged;
    private TimeSpan linkedAt, retreatAt, activeAt;

    private sealed record Command(byte Device, byte Feature, byte Function, byte[] Parameters,
        string Name, bool Write, bool Cleanup, Func<bool> Allowed, Action<HidReport> Success,
        Action? Submitted = null);
    private sealed class Probe(byte device)
    {
        public byte Device { get; } = device;
        public bool PingConfirmed { get; set; }
        public byte NameFeature { get; set; }
        public byte? NameLength { get; set; }
        public List<byte> Name { get; } = [];
    }
    private sealed class Pending(Command command, int generation, TimeSpan deadline)
    {
        public Command Command { get; } = command;
        public int Generation { get; } = generation;
        public TimeSpan Deadline { get; set; } = deadline;
        public int Retries { get; set; }
        public bool Busy { get; set; }
    }

    public GapSession(IReportTransport transport, IInputSink input, IClock clock, ILog log, Func<bool> optionsRunning, AppSettings? config = null,
        Func<DateTime?>? optionsStartedUtc = null, Func<DateTime>? utcNow = null)
    {
        var c = config ?? new(); c.Validate();
        this.transport = transport; this.input = input; this.clock = clock; this.log = log;
        this.optionsRunning = optionsRunning;
        this.optionsStartedUtc = optionsStartedUtc;
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        createdUtc = this.utcNow();
        settings = sessionSettings = c.GapFiller;
        smoother = CreateSmoother(settings.ThumbWheel);
        paused = !settings.Enabled;
        state = paused ? SessionState.Paused : SessionState.Disconnected;
    }
    private bool supportedMouseSeen;
    public bool SupportedMouseSeen { get { lock (gate) return supportedMouseSeen; } }
    public SessionState State { get { lock (gate) return state; } }
    public bool IsPaused { get { lock (gate) return paused; } }
    public int WorkerWaitMilliseconds
    {
        get
        {
            lock (gate)
            {
                TimeSpan? due = pending?.Deadline;
                if (!paused && pending == null && ((device == 0 && (probe != null || probes.Count > 0)) || cleanup.Count > 0))
                    return 1;
                if (!paused && state == SessionState.Retreated && !startupCheck && (!cleanupDone || bridgeActive))
                {
                    var deadline = retreatAt + TimeSpan.FromSeconds(sessionSettings.CleanupDelaySeconds);
                    if (!due.HasValue || deadline < due.Value) due = deadline;
                }
                if (!paused && smoother.NextDue is { } wheelDue && (!due.HasValue || wheelDue < due.Value))
                    due = wheelDue;
                return due.HasValue ? (int)Math.Clamp(Math.Ceiling((due.Value - clock.Now).TotalMilliseconds), 1, int.MaxValue) : Timeout.Infinite;
            }
        }
    }
    public event Action? Wake;
    public bool TryTakeHorizontal(int delta)
    {
        if (!bridgeActive || delta == 0) return false;
        try { nativeWheel.Enqueue(delta); }
        catch { return false; }
        try { Wake?.Invoke(); }
        catch { }
        return true;
    }
    public event Action<SessionState>? StateChanged;
    // ロック内から呼ぶため、購読側は待たずに UI スレッドへ投函するだけにする。
    public event Action<string>? SwitchNotice;
    public IReadOnlyDictionary<ushort, byte> OptionsFlags { get { lock (gate) return new Dictionary<ushort, byte>(optionsFlags); } }

    public void UpdateSettings(AppSettings config)
    {
        config.Validate();
        lock (gate)
        {
            bool enabledChanged = settings.Enabled != config.GapFiller.Enabled;
            settings = config.GapFiller;
            // 要確認: 接続中の HID 設定変更は次の接続へ延期。速度・方向は即時反映する。
            wheel.Reset();
            nativeScaler.Reset();
            smoother = CreateSmoother(settings.ThumbWheel);
            if (enabledChanged) SetPaused(!settings.Enabled);
            log.Write("設定再読込: HID 設定は次の接続、ホイール速度・方向は即時反映");
        }
    }
    private static IWheelSmoother CreateSmoother(ThumbSettings thumb)
    {
        IWheelSmoother result = thumb.SmoothMode == "spring" ? new WheelSpringSmoother() : new WheelSmoother();
        result.Configure(thumb.SmoothMode == "spring" ? thumb.SpringMs : thumb.SmoothMs, thumb.SmoothIntervalMs);
        return result;
    }

    public void SetPaused(bool value)
    {
        lock (gate)
        {
            StopBridge(value ? "一時停止" : "再開");
            paused = value;
            if (value) smoother.Reset();
            if (device != 0) previousActive.Remove((receiverIdentity, device));
            probes.Clear(); probe = null;
            Invalidate(); cleanupDone = true; startupCheck = false;
            heldButtons.Clear();
            Change(value ? SessionState.Paused : SessionState.Retreated, value ? "ユーザー一時停止" : "再開: 次の接続を待つ");
        }
        Wake?.Invoke();
    }
    public void ReceiverLost()
    {
        lock (gate)
        {
            StopBridge("レシーバー切断");
            RememberSession();
            Invalidate(); device = 0; identities.Clear(); features.Clear(); probes.Clear(); probe = null; startupCheck = false;
            heldButtons.Clear();
            Change(paused ? SessionState.Paused : SessionState.Disconnected, "レシーバー切断/復帰時の再オープン");
        }
        Wake?.Invoke();
    }
    public void ReceiverOpened(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        lock (gate)
        {
            if (receiverIdentity == identity) return;
            ReceiverLost();
            receiverIdentity = identity;
        }
    }
    public void ProbeExisting()
    {
        lock (gate)
        {
            if (paused || device != 0) return;
            Invalidate(); probe = null;
            probes.Clear();
            for (byte d = 1; d <= 6; d++) probes.Enqueue(d);
            log.Write("起動判定: slots 1〜6 ping、成功した番号だけ DEVICE_NAME で特定");
        }
        Wake?.Invoke();
    }

    public void Receive(ReadOnlySpan<byte> raw)
    {
        try
        {
            var report = HidReport.Parse(raw);
            if (report is null) return;
            lock (gate)
            {
                if (report.Kind == ReportKind.Connection && report.Device is >= 1 and <= 6)
                {
                    if (report.Wpid != 0xB042) previousActive.Remove((receiverIdentity, report.Device));
                    identities[report.Device] = report.Wpid;
                    if (report.Wpid == 0xB042 && report.LinkUp)
                    {
                        Begin(report.Device, false);
                    }
                    else if (report.Device == device)
                    {
                        StopBridge("リンク切断");
                        RememberSession();
                        if (report.Wpid != 0xB042) previousActive.Remove((receiverIdentity, report.Device));
                        Invalidate(); device = 0; startupCheck = false;
                        heldButtons.Clear();
                        Change(paused ? SessionState.Paused : SessionState.Disconnected, "マウスのリンク切断/機器変更");
                    }
                }
                if (!paused && report.Device == device)
                {
                    // Options+ の swid は起動ごとに変わる（0xA・0xB・0xC・0xE を確認）。0（通知）と 5（自分）以外はすべて他のソフトとして扱う。
                    if (report.Kind == ReportKind.Response && report.Swid != 0 && report.Swid != 5)
                    {
                        // Discovery may not yet know the relevant index. Recheck before any write.
                        if (discoveryStep < FeatureOrder.Length && earlyOptions.Count < 256) earlyOptions.Add(report);
                        ObserveOptions(report);
                    }
                    if (report.Kind == ReportKind.Notification)
                    {
                        if (device != 0) ObserveIgnoredInput(report);
                        if (state == SessionState.Active) ObserveInput(report);
                    }
                    if (blocked && report.Swid != 5 && report.Kind is ReportKind.Notification or ReportKind.Response)
                        blocked = false;
                }
                Match(report);
            }
        }
        catch (Exception ex) { log.Write($"受信処理エラー: {ex}"); }
        finally { Wake?.Invoke(); }
    }

    private void Begin(byte d, bool existing)
    {
        supportedMouseSeen = true; // 接続通知・従来の起動識別で確認済み。切断しても記憶する。
        StopBridge("新しい接続");
        RememberSession();
        inheritedActive = previousActive.Contains((receiverIdentity, d));
        Invalidate(); probes.Clear(); probe = null; device = d; features.Clear(); owned.Clear(); earlyOptions.Clear();
        heldButtons.Clear(); ignoredThumbLogged = false;
        optionsFlags.Clear(); optionsThumb = thumbOwned = cleanupDone = blocked = false;
        discoveryStep = applyStep = 0; linkedAt = clock.Now; sessionSettings = settings;
        dpi = sessionSettings.Dpi.Primary; startupCheck = !paused;
        Change(paused ? SessionState.Paused : SessionState.Retreated,
            existing ? "起動時の確認済みマウス: 横取り状態を確認" : $"リンク確立 dev={d} WPID=B042: 横取り状態を確認");
    }
    private void RememberSession()
    {
        if (device == 0) return;
        if (state == SessionState.Active) previousActive.Add((receiverIdentity, device));
        else previousActive.Remove((receiverIdentity, device));
    }
    private void Invalidate()
    {
        int thumbCount = 0;
        foreach (var a in actions)
        {
            if (a.Cid != 0)
                log.Write($"未実行のボタン操作を破棄 CID={a.Cid:X4} 設定={a.Action!.Action} {a.Action.Keys}");
            else thumbCount++;
        }
        if (thumbCount > 0) log.Write($"未送信のサムホイール {thumbCount} 件を破棄");
        generation++; pending = null; actions.Clear(); cleanup.Clear(); pressed.Clear(); wheel.Reset();
    }
    private void Change(SessionState next, string reason)
    {
        var old = state; state = next;
        if (next == SessionState.Active)
        {
            activeAt = clock.Now;
            if (!paused && sessionSettings.ThumbWheel.Mode == "hook" && !bridgeActive)
            {
                nativeScaler.Reset();
                bridgeReceived = bridgeReceivedTotal = bridgeSent = bridgeSentTotal = 0;
                bridgeExamples.Clear(); bridgeFailureLogged = false;
                bridgeActive = true;
                log.Write($"サムホイール: フック方式で橋渡しを開始（向き反転・速度 {settings.ThumbWheel.SpeedPercent}%）");
            }
        }
        log.Write($"状態 {old} -> {next}: {reason} monotonic={clock.Now.TotalMilliseconds:F3}ms");
        StateChanged?.Invoke(next);
    }
    private void StopBridge(string reason)
    {
        if (!bridgeActive) return;
        bridgeActive = false;
        log.Write($"サムホイール: 橋渡しを終了（{reason}） 受け取り {bridgeReceived} 件（合計 {bridgeReceivedTotal}）、送信 {bridgeSent} 件（合計 {bridgeSentTotal}）、元の量の例 [{string.Join(",", bridgeExamples)}]");
        bridgeReceived = bridgeReceivedTotal = bridgeSent = bridgeSentTotal = 0;
        bridgeExamples.Clear(); nativeScaler.Reset();
        // 要確認: 終了後の残りキューも同じ橋渡しとして、送信失敗の記録済み状態は次の開始まで保持する。
    }
    private void NotifySwitch(string text)
    {
        if (!settings.NotifyOnSwitch) return;
        log.Write($"通知: {text}");
        SwitchNotice?.Invoke(text);
    }
    private bool IsFeature(HidReport r, ushort id) => features.TryGetValue(id, out var f) && r.Feature == f;
    private void ObserveOptions(HidReport r)
    {
        bool cidWrite = IsFeature(r, 0x1B04) && r.Function == 3;
        bool thumbWrite = IsFeature(r, 0x2150) && r.Function == 2;
        bool dpiWrite = IsFeature(r, 0x2201) && r.Function == 3;
        if (!cidWrite && !thumbWrite && !dpiWrite) return;
        lastOptionsConfigUtc = utcNow();
        if (cidWrite && r.Parameters.Length >= 3) optionsFlags[HidReport.U16(r.Parameters)] = r.Parameters[2];
        if (thumbWrite)
        {
            optionsThumb = true;
            StopBridge("Options+ がサムホイールを設定");
        }
        if (state == SessionState.Active || startupCheck)
        {
            bool wasActive = state == SessionState.Active;
            Invalidate(); startupCheck = false; retreatAt = clock.Now;
            Change(SessionState.Retreated, $"Options+ 設定開始（swid={r.Swid:X}）: {(cidWrite ? "setCidReporting" : thumbWrite ? "setReporting" : "setSensorDpi")}");
            if (wasActive)
                NotifySwitch(FormattableString.Invariant($"Options+ に引き継ぎました（肩代わり {(retreatAt - activeAt).TotalSeconds:F2} 秒）"));
        }
    }
    private void ObserveIgnoredInput(HidReport r)
    {
        string reason = startupCheck ? "接続判定中" : "Options+ に引き継ぎ済み";
        // 要確認: 不完全なボタン通知は既存の ObserveInput と同じく無視し、押下履歴も保持する。
        if (IsFeature(r, 0x1B04) && r.Function == 0 && r.Parameters.Length >= 8)
        {
            HashSet<ushort> now = [];
            for (int i = 0; i < 8; i += 2)
            {
                ushort cid = HidReport.U16(r.Parameters.AsSpan(i, 2));
                if (cid == 0 || !now.Add(cid) || heldButtons.Contains(cid)) continue;
                if (state != SessionState.Active && sessionSettings.Buttons.TryGetValue($"0x{cid:X4}", out var action))
                    log.Write($"ボタン CID={cid:X4} 押下: PartsTray は実行しない（{reason}） 設定={action.Action} {action.Keys}");
            }
            heldButtons = now;
        }
        if (state != SessionState.Active && IsFeature(r, 0x2150) && r.Function == 0 &&
            r.Parameters.Length >= 6 && !ignoredThumbLogged && BinaryPrimitives.ReadInt16BigEndian(r.Parameters) != 0)
        {
            ignoredThumbLogged = true;
            log.Write($"サムホイール回転を検出: PartsTray は送信しない（{reason}）※このリンクでは 1 回だけ記録");
        }
    }
    private void ObserveInput(HidReport r)
    {
        if (IsFeature(r, 0x1B04) && r.Function == 0 && r.Parameters.Length >= 8)
        {
            HashSet<ushort> now = [];
            for (int i = 0; i < 8; i += 2)
            {
                ushort cid = HidReport.U16(r.Parameters.AsSpan(i, 2));
                if (cid == 0 || !now.Add(cid) || pressed.Contains(cid) || !owned.Contains(cid)) continue;
                if (sessionSettings.Buttons.TryGetValue($"0x{cid:X4}", out var action))
                    actions.Enqueue((generation, cid, action, 0));
            }
            pressed = now;
        }
        if (IsFeature(r, 0x2150) && r.Function == 0 && thumbOwned && r.Parameters.Length >= 6)
        {
            // 要確認: 回転は先頭 int16 BE。ログの +36.999 秒等に +1 があるが方向・単位は実機で確認。
            short rotation = BinaryPrimitives.ReadInt16BigEndian(r.Parameters);
            int delta = wheel.Convert(rotation, settings.ThumbWheel.SpeedPercent, settings.ThumbWheel.DirectionSign);
            if (delta != 0) actions.Enqueue((generation, 0, null, delta));
        }
    }
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
                    if (paused) return;
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
    private Command? NextCommand()
    {
        if (device != 0 && (state == SessionState.Active || startupCheck) && !blocked)
        {
            if (discoveryStep < FeatureOrder.Length)
            {
                ushort id = FeatureOrder[discoveryStep];
                return Read(0, 0, HidReport.Be(id), $"ROOT.getFeature {id:X4}", r =>
                {
                    if (r.Parameters[0] == 0 || r.Parameters[0] >= 0x80 || features.Values.Contains(r.Parameters[0]))
                        throw new InvalidDataException($"必須機能が無い/不正: {id:X4}");
                    features[id] = r.Parameters[0]; discoveryStep++;
                    foreach (var seen in earlyOptions) ObserveOptions(seen);
                });
            }
            if (startupCheck)
                return Read(features[0x1B04], 2, HidReport.Be(0x52), "接続判定 getCidReporting", r =>
                {
                    bool running = optionsRunning(); // Query at the decision, not at receiver open.
                    bool divert = (r.Parameters[2] & 1) != 0;
                    bool fresh = false;
                    DateTime? started = null;
                    string optionsStartLog = "";
                    if (divert && running && !inheritedActive && optionsStartedUtc != null)
                    {
                        try
                        {
                            started = optionsStartedUtc();
                            fresh = started is { } s && s > createdUtc &&
                                (lastOptionsConfigUtc is not { } seen || seen < s);
                        }
                        catch { started = null; fresh = false; }
                        // 要確認: 起動時刻が不明なら「起動後」の観測を確認できないため「なし」と記録し、判定は従来どおり撤退する。
                        bool configured = started is { } start && lastOptionsConfigUtc is { } seenAt && seenAt >= start;
                        string startTime = started?.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) ?? "不明";
                        optionsStartLog = $" Options+起動={startTime} 起動後の設定={(configured ? "あり" : "なし")}";
                    }
                    startupCheck = false;
                    log.Write($"接続判定: divert={(r.Parameters[2] & 1)} Options+={running} previousActive={inheritedActive}{optionsStartLog}");
                    if (divert && running && !inheritedActive && !fresh)
                    { cleanupDone = true; Change(SessionState.Retreated, "接続判定: Options+ 設定済み"); }
                    else
                    {
                        Change(SessionState.Active, fresh
                            ? "接続判定: Options+ は起動後まだ設定していない（前の横取りが残っている）"
                            : "接続判定: 設定の肩代わりが必要");
                        // 要確認: 通知の秒数は OS の表示言語によらず小数点と 2 桁を固定する。
                        NotifySwitch(FormattableString.Invariant($"肩代わりを開始しました（接続から {(activeAt - linkedAt).TotalSeconds:F2} 秒）"));
                    }
                });
            if (applyStep == 0)
                return Write(features[0x2201], 3, DpiBytes(sessionSettings.Dpi.Primary), "setSensorDpi 初期適用", _ =>
                { applyStep++; }, submitted: () => log.Write($"適用開始 link+{(clock.Now - linkedAt).TotalMilliseconds:F3}ms"));
            if (applyStep <= ButtonOrder.Count)
            {
                ushort cid = ButtonOrder[applyStep - 1];
                if (!sessionSettings.Buttons.ContainsKey($"0x{cid:X4}")) { applyStep++; return NextCommand(); }
                return Write(features[0x1B04], 3, CidBytes(cid, 3), $"setCidReporting ON {cid:X4}", _ => applyStep++, submitted: () => owned.Add(cid));
            }
            if (applyStep == 6)
            {
                if (sessionSettings.ThumbWheel.Mode == "off") { applyStep = 8; CompleteApply(); return NextCommand(); }
                if (sessionSettings.ThumbWheel.Mode == "hook")
                    return Read(features[0x2150], 1, [], "THUMB_WHEEL getStatus 残りの横取りを確認", r =>
                    {
                        var p = r.Parameters;
                        if (p.Length < 2)
                        {
                            // 要確認: 2 バイト未満の応答は横取りなしとして扱い、状態が不明なまま解除を書き込まない。
                            log.Write("サムホイール: 横取りは残っていない（応答が 2 バイト未満）");
                            applyStep = 8; CompleteApply();
                        }
                        else if ((p[0] & 1) != 0)
                        {
                            log.Write($"サムホイール: 横取りが残っていたので解除する（{p[0]:X2} {p[1]:X2}）");
                            applyStep = 7;
                        }
                        else
                        {
                            log.Write($"サムホイール: 横取りは残っていない（{p[0]:X2} {p[1]:X2}）");
                            applyStep = 8; CompleteApply();
                        }
                    });
                // 要確認: setReporting(mode,invert)=(1,1) は推定。直後に getStatus を検証する。
                return Write(features[0x2150], 2, [1, 1], "THUMB_WHEEL setReporting ON", _ => applyStep++, submitted: () => thumbOwned = true);
            }
            if (applyStep == 7)
            {
                if (sessionSettings.ThumbWheel.Mode == "hook")
                    return Write(features[0x2150], 2, [0, 0], "THUMB_WHEEL 残りの横取りを解除", _ =>
                    { applyStep = 8; CompleteApply(); });
                return Read(features[0x2150], 1, [], "THUMB_WHEEL getStatus 読み戻し", r =>
                {
                    var p = r.Parameters;
                    log.Write(p[0] == 1 && (p[1] & 1) == 1
                        ? $"サムホイール読み戻し OK {p[0]:X2} {p[1]:X2}"
                        : $"サムホイール読み戻し不一致 {p[0]:X2} {p[1]:X2}: 要確認");
                    applyStep++; CompleteApply();
                });
            }
        }
        while (actions.TryDequeue(out var a))
        {
            if (a.Generation != generation || state != SessionState.Active) continue;
            if (a.Action == null) { input.HorizontalWheel(a.Delta); continue; }
            log.Write($"ボタン CID={a.Cid:X4} 操作={a.Action.Action} {a.Action.Keys}");
            if (a.Action.Action == "ToggleDpi")
            {
                dpi = dpi == sessionSettings.Dpi.Primary ? sessionSettings.Dpi.Secondary : sessionSettings.Dpi.Primary;
                return Write(features[0x2201], 3, DpiBytes(dpi), $"setSensorDpi ボタン {dpi}", _ => { });
            }
            input.Keys(a.Action.Keys!);
        }
        if (state == SessionState.Retreated && !startupCheck && !cleanupDone && clock.Now >= retreatAt + TimeSpan.FromSeconds(sessionSettings.CleanupDelaySeconds))
            PrepareCleanup();
        while (cleanup.TryDequeue(out var c)) if (c.Allowed()) return c;
        return device == 0 ? NextProbe() : null;
    }
    private Command? NextProbe()
    {
        if (probe == null)
        {
            if (!probes.TryDequeue(out byte d)) return null;
            probe = new(d);
        }
        var candidate = probe;
        bool Allowed() => !paused && device == 0 && probe == candidate;
        Command ReadName(byte feature, byte fn, byte[] parameters, string name, Action<HidReport> success) =>
            new(candidate.Device, feature, fn, parameters, name, false, false, Allowed, success);
        if (!candidate.PingConfirmed)
            return ReadName(0, 1, [0, 0, 0x5A], $"起動 ping dev={candidate.Device}", r =>
            {
                if (r.Parameters.Length < 3 || r.Parameters[2] != 0x5A) { probe = null; return; }
                candidate.PingConfirmed = true;
                if (identities.GetValueOrDefault(candidate.Device) == 0xB042) Begin(candidate.Device, true);
            });
        if (candidate.NameFeature == 0)
            return ReadName(0, 0, [0, 5], "識別 ROOT.getFeature 0005", r =>
            {
                if (r.Parameters[0] is 0 or >= 0x80) { probe = null; return; }
                candidate.NameFeature = r.Parameters[0];
            });
        if (!candidate.NameLength.HasValue)
            return ReadName(candidate.NameFeature, 0, [], "DEVICE_NAME 長さ", r =>
            {
                if (r.Parameters[0] == 0) { probe = null; return; }
                candidate.NameLength = r.Parameters[0];
            });
        return ReadName(candidate.NameFeature, 1, [(byte)candidate.Name.Count], "DEVICE_NAME 本文", r =>
        {
            int count = Math.Min(candidate.NameLength.Value - candidate.Name.Count, r.Parameters.Length);
            candidate.Name.AddRange(r.Parameters.Take(count));
            if (candidate.Name.Count != candidate.NameLength.Value) return;
            if (candidate.Name.SequenceEqual("MX Master 4"u8.ToArray()))
            {
                identities[candidate.Device] = 0xB042;
                Begin(candidate.Device, true);
            }
            else
            {
                log.Write($"識別 dev={candidate.Device}: 対象外。これ以上の照会/書込みはしない");
                previousActive.Remove((receiverIdentity, candidate.Device));
                probe = null;
            }
        });
    }
    private void CompleteApply() => log.Write($"適用完了 link+{(clock.Now - linkedAt).TotalMilliseconds:F3}ms");
    private Command Read(byte f, byte fn, byte[] p, string name, Action<HidReport> success, Func<bool>? allowed = null, bool cleaning = false)
        => new(device, f, fn, p, name, false, cleaning, allowed ?? (() => !paused && device != 0 && (state == SessionState.Active || startupCheck)), success);
    private Command Write(byte f, byte fn, byte[] p, string name, Action<HidReport> success, bool cleaning = false, Func<bool>? allowed = null, Action? submitted = null)
        => new(device, f, fn, p, name, true, cleaning, allowed ?? (() => !paused && state == SessionState.Active), success, submitted);
    private static byte[] DpiBytes(int value) => [0, (byte)(value >> 8), (byte)value];
    private static byte[] CidBytes(ushort cid, byte flags) => [(byte)(cid >> 8), (byte)cid, flags, (byte)(cid >> 8), (byte)cid];
    private bool CidNeedsCleanup(ushort cid) => !optionsFlags.TryGetValue(cid, out byte flags) || (flags & 1) == 0;
    private void PrepareCleanup()
    {
        cleanupDone = true;
        int gen = generation;
        log.Write("後片付け開始（このセッションで 1 回のみ）");
        foreach (ushort cid in owned.ToArray())
        {
            bool Allowed() => !paused && generation == gen && state == SessionState.Retreated && CidNeedsCleanup(cid);
            if (!Allowed()) continue;
            cleanup.Enqueue(Read(features[0x1B04], 2, HidReport.Be(cid), $"後片付け getCidReporting {cid:X4}", r =>
            {
                if ((r.Parameters[2] & 1) != 0 && Allowed())
                    cleanup.Enqueue(Write(features[0x1B04], 3, CidBytes(cid, 2), $"後片付け OFF {cid:X4}", _ => { }, true, Allowed));
            }, Allowed, true));
        }
        bool ThumbAllowed() => !paused && generation == gen && state == SessionState.Retreated && thumbOwned && !optionsThumb;
        if (ThumbAllowed())
            cleanup.Enqueue(Read(features[0x2150], 1, [], "後片付け getThumbStatus", r =>
            {
                if ((r.Parameters[0] & 1) != 0 && ThumbAllowed())
                    cleanup.Enqueue(Write(features[0x2150], 2, [0, 0], "後片付け THUMB OFF", _ => { }, true, ThumbAllowed));
            }, ThumbAllowed, true));
    }
}
