// 高速スクロールと内側への届け直しを、指定された回転量で1回送ります。
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class GestureModule
{
    private void SendFastScroll(GestureEffect e, uint keys, ScrollProbeLog wheelLog)
    {
        bool sent = false;
        var input = e.Input;
        var target = e.Target;
        try
        {
            (input, target) = services.PrepareFastScroll(e);
            sent = target.Window == 0 ? services.Replay(input)
                : target.PostWheel(input, keys, N.PostMessageW);
        }
        finally
        {
            if (!sent) Reply(engine.WheelSendFailed);
            if (Volatile.Read(ref settings).Diagnostics.ScrollProbe)
                wheelLog.RecordWheel(input, target, sent, Environment.TickCount64);
        }
    }

    public bool TryRedirectWheel(PointerEvent input, EdgeWheelTarget target)
        => target.PostWheel(input, WheelKeys(input), N.PostMessageW);

    private static uint WheelKeys(PointerEvent input)
    {
        uint keys = 0;
        if ((input.Modifiers & PointerModifiers.Shift) != 0) keys |= 0x04;
        if ((input.Modifiers & PointerModifiers.Control) != 0) keys |= 0x08;
        // GetAsyncKeyState の左右は物理ボタンなので、wParam には入れ替え設定を反映する。
        bool swapped = N.GetSystemMetrics(23) != 0; // SM_SWAPBUTTON
        if (KeyDown(0x01)) keys |= swapped ? 0x02u : 0x01u;
        if (KeyDown(0x02)) keys |= swapped ? 0x01u : 0x02u;
        if (KeyDown(0x04)) keys |= 0x10; // 中
        if (KeyDown(0x05)) keys |= 0x20; // 戻る
        if (KeyDown(0x06)) keys |= 0x40; // 進む
        return keys;
    }

    private static bool KeyDown(int virtualKey) => (N.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
