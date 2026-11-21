// ヒットテスト・入力再送・ウィンドウ操作をフックの外で実行します。
// ジェスチャーの効果や右ホイールの切り替え実行を変えるときに開きます。
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class GestureModule
{
    private void Decisions()
    {
        while (Volatile.Read(ref workerStop) == 0)
        {
            while (decisions.TryDequeue(out var e))
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
                        var cache = services.Probe(e.Input.Position, e.At);
                        Reply(() => engine.SetCache(cache));
                    }
                }
                catch (Exception ex)
                {
                    log.Write($"ヒットテスト/UIA: {ex}");
                    if (e.Kind == GestureEffectKind.HitTest) Reply(() => engine.Resolve(e.Token, default));
                }
            }
            decisionWake.WaitOne(100);
        }
    }
    private void Actions()
    {
        while (Volatile.Read(ref workerStop) == 0 || !actions.IsEmpty)
        {
            Volatile.Write(ref actionHeartbeat, Environment.TickCount64);
            while (actions.TryDequeue(out var e))
            {
                try
                {
                    if (e.Kind == GestureEffectKind.Replay)
                    {
                        bool ok = services.Replay(e.Input);
                        if (!ok && e.Input.Kind == MouseEventKind.Wheel) Reply(engine.WheelSendFailed);
                        if (e.Input.Kind == MouseEventKind.Down) Reply(() => engine.ReplayCompleted(e.Token));
                    }
                    else if (e.Kind == GestureEffectKind.Flush) Reply(() => Volatile.Write(ref hookExit, 1));
                    else if (e.Kind == GestureEffectKind.RightWheelReport) log.Write(e.Text!);
                    else if (e.Kind == GestureEffectKind.Warning) log.Write($"{e.Text} {e.At.TotalMilliseconds:F3}ms");
                }
                catch (Exception ex)
                {
                    log.Write($"ジェスチャー送信/操作: {ex}");
                    if (e.Kind == GestureEffectKind.Replay && e.Input.Kind == MouseEventKind.Wheel) Reply(engine.WheelSendFailed);
                }
                Volatile.Write(ref actionHeartbeat, Environment.TickCount64);
            }
            actionWake.WaitOne(100);
        }
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
