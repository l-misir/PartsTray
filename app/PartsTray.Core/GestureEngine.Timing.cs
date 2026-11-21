// ジェスチャー判定の待ち時間とポインター情報の更新を管理します。
// 入力判定の時間切れや情報更新の間隔を変えるときに開きます。
namespace PartsTray.Core;

public sealed partial class GestureEngine
{
    public void WheelSendFailed()
    {
        wheelDisabledUntil = clock.Now + TimeSpan.FromSeconds(60);
        Emit(GestureEffectKind.Warning, text: "ホイール SendInput 失敗: 高速スクロールの横取りを60秒停止");
    }
    public void Tick()
    {
        if (probeActiveUntil.HasValue && clock.Now > probeActiveUntil.Value)
        { probeActiveUntil = null; probePending = false; }
        if (!paused && probeActiveUntil.HasValue && clock.Now >= nextProbe && (probePending || clock.Now >= refreshProbeAt))
        {
            SendProbe();
        }
        // Maximum 3 current buttons plus already released decisions; iteration only when pending.
        if (pending.Count != 0)
        {
            expired.Clear();
            foreach (var c in pending.Values)
                if (clock.Now - c.At >= TimeSpan.FromSeconds(2)) expired.Add(c);
            foreach (var c in expired)
            { Replay(c); Emit(GestureEffectKind.Warning, text: "ボタン判定が2秒未確定: 送り直して解除"); }
        }
        var right = buttons[(int)PointerButton.Right];
        if (right == null || right.Phase is not (Phase.Pending or Phase.Gesture)) return;
        // 右ホイール有効時は、解放まで押下を保持して長押し後の切り替えも受け付ける。
        bool waitForWheel = right.Settings.Gesture.Enabled && right.Settings.Gesture.RightWheelSwitch;
        if (!waitForWheel && !right.Stroke.HasStroke && clock.Now - right.At >= TimeSpan.FromMilliseconds(right.Settings.Gesture.StartTimeoutMs))
        { Replay(right); return; }
        if (right.Phase == Phase.Gesture && right.Stroke.HasStroke && !tip &&
            clock.Now - stillSince >= TimeSpan.FromMilliseconds(right.Settings.Gesture.TooltipDelayMs))
        {
            tip = true; Emit(GestureEffectKind.Tooltip, right, new(MouseEventKind.Move, position), right.Stroke.Description);
        }
    }
    private void RequestProbe()
    {
        probeActiveUntil = clock.Now + TimeSpan.FromSeconds(3);
        if (clock.Now >= nextProbe) SendProbe();
        else probePending = true;
    }
    private void SendProbe()
    {
        probePending = false;
        nextProbe = clock.Now + TimeSpan.FromMilliseconds(30);
        refreshProbeAt = clock.Now + TimeSpan.FromMilliseconds(50);
        Emit(GestureEffectKind.Probe, input: new(MouseEventKind.Move, position));
    }
}
