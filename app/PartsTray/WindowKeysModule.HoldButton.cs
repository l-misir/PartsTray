// HID++ の押下一覧を UI スレッドで受け取り、ボタンを離すまでホイールで選びます。
// ボタン選択の開始・安全終了・初回の受信記録を変えるときに開きます。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class WindowKeysModule
{
    private readonly WindowHoldSelection holdSelection;
    private readonly HashSet<ushort> receivedHoldButtons = [];
    private bool holdSession;

    // TrayContext.Post で UI スレッドへ移してから呼ぶ。HID の gate は取得しない。
    public void ReceivePressedButtons(IReadOnlyList<ushort> buttons)
    {
        try
        {
            if (current is not { } keys || !WindowHoldButton.TryParse(keys.HoldButton, out ushort cid) || cid == 0) return;
            if (buttons.Contains(cid) && receivedHoldButtons.Add(cid))
                log.Write($"{WindowHoldButton.Label(keys.HoldButton)}の押下を受け取りました（機器のボタン通知）");

            // 開始前にキーによる選択を閉じ、集計を混ぜない。押下だけでは Switch しない。
            if (!holdSelection.IsPressed && buttons.Contains(cid)) EndWheelSelection();
            var change = holdSelection.ReceiveButtons(buttons);
            if (change == HoldButtonChange.Ended) EndWheelSelection();
            else if (change == HoldButtonChange.Started)
            {
                holdSession = true;
                switcher.BeginSession();
                InstallWheelHook();
            }
        }
        catch (Exception ex)
        {
            EndWheelSelection();
            log.Write($"ボタン＋ホイールの処理に失敗: {ex.Message}");
        }
    }

    private bool SelectionExpired() => holdSelection.Expire() || wheel.Expire();
}
