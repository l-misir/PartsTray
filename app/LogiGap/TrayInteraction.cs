using System.Runtime.InteropServices;
using System.Text;

namespace LogiGap;

// 通知領域のアイコンを並べ替えている（タスクバーの上でマウスのボタンを押している）間は、アイコンを描き替えない。
// 11/19 ユーザー：「表示位置の変更ができない（Core Temp でも同じ）」。アイコンやツールチップを頻繁に変えると、
// 通知領域が並びを作り直すため、ドラッグの途中で並べ替えが取り消されると考えられる（Core Temp は 1 秒ごと、
// LogiGap はオウムが 20〜250ms ごと、温度・メモリが 200ms ごとに描き替える）。
internal static class TrayInteraction
{
    private static readonly string[] TrayClasses =
    [
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd",                       // タスクバー
        "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow", // 隠れているアイコンの小窓（Windows 11 / 10）
        "Xaml_WindowedPopupClass"
    ];
    private static long busyUntil;

    // 押している間と、離してから 0.6 秒は true。UI スレッドのタイマーから呼ぶ。
    public static bool Busy
    {
        get
        {
            long now = Environment.TickCount64;
            try
            {
                bool pressed = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
                if (pressed && GetCursorPos(out var point) && IsTray(WindowFromPoint(point))) busyUntil = now + 600;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            return now < busyUntil;
        }
    }

    private static bool IsTray(nint window)
    {
        if (window == 0) return false;
        nint root = GetAncestor(window, 2);   // GA_ROOT
        var name = new StringBuilder(64);
        if (GetClassName(root == 0 ? window : root, name, name.Capacity) == 0) return false;
        return TrayClasses.Contains(name.ToString());
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
}
