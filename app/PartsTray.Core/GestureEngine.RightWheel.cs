// 右ボタンを押している間のホイールを取り込み、切り替え効果と集計を作ります。
// 右ホイールの取り込み条件・ジェスチャー抑止・押下ごとの記録を変えるときに開きます。
namespace PartsTray.Core;

public sealed partial class GestureEngine
{
    public bool IsRightButtonDown => buttons[(int)PointerButton.Right] != null;

    // OS 側で解放済みと確認したとき専用。Tick・Replay・FinishGesture は呼ばない。
    public void RightButtonReleasedElsewhere()
    {
        if (buttons[(int)PointerButton.Right] is not { } c) return;
        ReportRightWheel(c);
        buttons[(int)PointerButton.Right] = null;
        pending.Remove(c.Token);
        foreach (var child in pending.Values.Where(x => x.ParentGesture == c.Token).ToArray())
        { pending.Remove(child.Token); child.Phase = Phase.Consumed; }
        c.Phase = Phase.Consumed;
        tip = false;
        Emit(GestureEffectKind.TrailEnd, c);
        Emit(GestureEffectKind.Tooltip, c);
        Emit(GestureEffectKind.Warning, c, text: "右ボタンはよそで離されたため押下を終了");
    }

    public long RightPressToken => buttons[(int)PointerButton.Right]?.Token ?? 0;

    public bool ReceiveDeviceRotation(short rotation)
    {
        if (paused || !settings.WindowKeys.ThumbWheelFromDevice || rotation == 0) return false;
        return TakeRightWheel(new(MouseEventKind.HorizontalWheel, position), rotation);
    }

    private bool TakeRightWheel(PointerEvent input, short? rotation = null)
    {
        if (!settings.Gesture.Enabled || !settings.Gesture.RightWheelSwitch ||
            buttons[(int)PointerButton.Right] is not { } c ||
            (c.Phase is not (Phase.Pending or Phase.Gesture) && c.RightWheel?.IsActive != true))
            return false;
        if (c.RightWheel == null)
        {
            c.RightWheel = new(() => clock.Now);
            c.RightWheel.Start(c.Settings.WindowKeys, untilRelease: true);
            pending.Remove(c.Token); // 遅いヒットテストからクリックや命令を復活させない。
            c.Phase = Phase.Consumed;
            foreach (var child in pending.Values.Where(x => x.ParentGesture == c.Token).ToArray())
            {
                pending.Remove(child.Token);
                child.Phase = Phase.Consumed;
            }
            tip = false;
            Emit(GestureEffectKind.TrailEnd, c); // 軌跡と吹き出しを両方消す。
        }
        var result = rotation is { } raw ? c.RightWheel.ReceiveDeviceRotation(raw)
            : c.RightWheel.Receive(input.Kind == MouseEventKind.HorizontalWheel, input.Delta, input.ExtraInfo);
        if (result.Steps != 0)
            effects.Post(new(GestureEffectKind.SwitchMaximized, c.Token, At: clock.Now, Steps: result.Steps));
        return result.Handled;
    }

    private void ReportRightWheel(Capture c)
    {
        if (c.RightWheel == null || c.WheelReported) return;
        c.WheelReported = true;
        c.RightWheel.Stop();
        Emit(GestureEffectKind.RightWheelReport, c, text: "右ボタン＋" + c.RightWheel.Summary);
    }
}
