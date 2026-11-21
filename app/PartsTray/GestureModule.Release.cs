// 右ボタンの実際の状態をフック所有スレッドで確かめ、解放の取りこぼしを終えます。
// 左右入れ替え時の確認や、よそで離した入力の扱いを変えるときに開きます。
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class GestureModule
{
    private bool ReleaseRightButtonIfNeeded(MouseEventKind kind)
    {
        // 11/21 実機：この確かめで、右ドラッグのジェスチャーが毎回すぐ終わってしまった（「右ボタンはよそで離されたため押下を終了」が
        // 右ドラッグのたびに出ていた）。ジェスチャーのために右ボタンの押下をフックで止めると、Windows の GetAsyncKeyState には
        // 押されたことが伝わらず、いつも「離されている」と読めるため。この確かめは使えないので止める。
        // 取りこぼしの元だった管理者の窓は、切り替えの候補から外してある（指示書R §2.3）。次の押下での取りこぼしの片付けは今までどおり。
        // 11/21 2 回目：条件だけ逆にした直しでは、右ボタンを離したとき（Up）に同じ誤判定が起き、ジェスチャーが右クリックになった。
        // どの入力でも確かめない。
        // 元の確かめ（GetAsyncKeyState で物理のボタンを見て engine.RightButtonReleasedElsewhere を呼ぶ）は、
        // 確かな見分け方（Raw Input など）が見つかるまで使わない。エンジン側の入口はそのまま残す。
        _ = kind;
        return false;
    }

    // HID のスレッドからは、受け取り時の押下番号を添えて投函するだけ。
    public void ReceiveDeviceRotation(short rotation)
    {
        long token = Volatile.Read(ref observedRightPressToken);
        if (token == 0 || Volatile.Read(ref stopping) != 0) return;
        Reply(() =>
        {
            if (!Enabled || engine.RightPressToken != token ||
                ReleaseRightButtonIfNeeded(MouseEventKind.HorizontalWheel)) return;
            engine.ReceiveDeviceRotation(rotation);
        });
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
