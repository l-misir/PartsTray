// ヒットテスト・入力再送・ウィンドウ操作をフックの外で実行します。
// ジェスチャーの効果や右ホイールの切り替え実行を変えるときに開きます。
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class GestureModule
{
    private void Decisions()
    {
        try { PrepareWheelWindows(); }
        finally { firstWindowScan.Set(); }
        while (Volatile.Read(ref workerStop) == 0)
        {
            PrepareWheelWindows();
            if (decisions.TryDequeue(out var e))
            {
                try
                {
                    if (!Enabled) { if (e.Kind == GestureEffectKind.HitTest) Reply(() => engine.Resolve(e.Token, default)); continue; }
                    if (e.Kind == GestureEffectKind.HitTest)
                    {
                        if (clock.Now - e.At >= TimeSpan.FromSeconds(2)) continue;
                        var hit = services.HitTest(e.Input, Volatile.Read(ref settings));
                        Reply(() => engine.Resolve(e.Token, Enabled ? hit : default));
                    }
                    else if (e.Kind == GestureEffectKind.Probe)
                    {
                        if (clock.Now - e.At > TimeSpan.FromMilliseconds(100)) continue;
                        int probeEpoch = Volatile.Read(ref epoch);
                        var cache = services.Probe(e.Input.Position, e.At);
                        Reply(() =>
                        {
                            if (Enabled && probeEpoch == Volatile.Read(ref epoch)) engine.SetCache(cache);
                        });
                    }
                }
                catch (Exception ex)
                {
                    log.Write($"ヒットテスト/UIA: {ex}");
                    if (e.Kind == GestureEffectKind.HitTest) Reply(() => engine.Resolve(e.Token, default));
                }
                // 判定の依頼が続いている間も、新しく開いた窓の準備を挟む。
                continue;
            }
            services.FlushProbeLog();
            decisionWake.WaitOne(100);
        }
        services.FlushProbeLog(stopping: true);
    }
    private void Actions()
    {
        var wheelLog = new ScrollProbeLog(log);
        while (Volatile.Read(ref workerStop) == 0 || !actions.IsEmpty)
        {
            Volatile.Write(ref actionHeartbeat, Environment.TickCount64);
            while (actions.TryDequeue(out var action))
            {
                var e = action.Effect;
                try
                {
                    if (e.Kind == GestureEffectKind.FastScroll) SendFastScroll(e, action.WheelKeys, wheelLog);
                    else if (e.Kind == GestureEffectKind.Replay)
                    {
                        services.Replay(e.Input);
                        if (e.Input.Kind == MouseEventKind.Down) Reply(() => engine.ReplayCompleted(e.Token));
                    }
                    else if (e.Kind == GestureEffectKind.Flush) Reply(() => Volatile.Write(ref hookExit, 1));
                    else if (e.Kind == GestureEffectKind.RightWheelReport) log.Write(e.Text!);
                    else if (e.Kind == GestureEffectKind.EdgeRedirectReport)
                    {
                        if (e.Valid) log.Write("画面の端のホイールを内側の子ウィンドウに届け直しました");
                        if (Volatile.Read(ref settings).Diagnostics.ScrollProbe)
                            wheelLog.RecordWheel(e.Input, e.Target, e.Steps == 1, Environment.TickCount64);
                    }
                    else if (e.Kind == GestureEffectKind.Warning) log.Write($"{e.Text} {e.At.TotalMilliseconds:F3}ms");
                }
                catch (Exception ex)
                {
                    log.Write($"ジェスチャー送信/操作: {ex}");
                }
                Volatile.Write(ref actionHeartbeat, Environment.TickCount64);
            }
            if (Volatile.Read(ref settings).Diagnostics.ScrollProbe) wheelLog.FlushDue(Environment.TickCount64);
            else wheelLog.Reset();
            actionWake.WaitOne(100);
        }
        wheelLog.Reset();
    }
    private void Commands()
    {
        while (Volatile.Read(ref workerStop) == 0)
        {
            while (commands.TryDequeue(out var work))
            {
                try
                {
                    var e = work.Effect;
                    bool Allowed() => Enabled && work.Epoch == Volatile.Read(ref epoch);
                    if (!Allowed()) continue;
                    if (e.Kind == GestureEffectKind.SwitchMaximized)
                    {
                        // UI のタイマーで前面化を確認するため、切り替えも UI スレッドへ渡す。
                        ui(() =>
                        {
                            if (!Allowed()) return;
                            try
                            {
                                if (windowSwitchToken != e.Token)
                                { windowSwitchToken = e.Token; windowSwitcher.BeginSession(); }
                                for (int i = 0; i < Math.Abs(e.Steps) && Allowed(); i++)
                                    windowSwitcher.Switch(e.Steps < 0, Allowed);
                            }
                            catch (Exception ex) { log.Write($"ウィンドウ切り替え: {ex}"); }
                        });
                    }
                    else if (e.Kind == GestureEffectKind.Command && e.Text != null)
                    {
                        if (e.Text == "MinimizeToTray") ui(() => { if (Allowed()) hidden.Hide(e.Window); });
                        else services.Execute(e.Window, e.Text, Volatile.Read(ref settings).Gesture, Allowed);
                    }
                    else if (e.Kind == GestureEffectKind.MoveWindow && N.IsWindow((nint)e.Window))
                    {
                        if (!N.SetWindowPos((nint)e.Window, 0, e.Input.Position.X, e.Input.Position.Y, 0, 0, 0x15 | 0x4000)) log.Write("無効ウィンドウの移動が拒否されました");
                    }
                }
                catch (Exception ex) { log.Write($"ウィンドウ操作: {ex}"); }
            }
            commandWake.WaitOne(100);
        }
    }
}
