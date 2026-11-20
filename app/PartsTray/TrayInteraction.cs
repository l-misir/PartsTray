// 通知領域のアイコンを並べ替えている間を見分けます。その間は、アイコンを描き替えません。
// 「並べ替え中」とみなす場所や時間を変えるときはここを編集します。
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace PartsTray;

// 通知領域のアイコンを並べ替えている（アイコンの上でマウスのボタンを押している）間は、アイコンを描き替えない。
// 11/19 ユーザー：「表示位置の変更ができない（Core Temp でも同じ）」。アイコンやツールチップを頻繁に変えると、
// 通知領域が並びを作り直すため、ドラッグの途中で並べ替えが取り消されると考えられる（Core Temp は 1 秒ごと、
// PartsTray はオウムが 20〜250ms ごと、温度・メモリが 200ms ごとに描き替える）。
// 11/20 ユーザー：「タスクバーをどこでもいいのでクリックするだけで止まる」。タスクバーは 1 枚の窓なので、
// 窓だけでは通知領域のアイコンの上かどうか分からない。押し始めの位置を UI Automation で調べ、
// 通知領域のアイコン（と ∧ のボタン）の上で押したときだけ止める。調べ終わるまでの一瞬は、念のため止める。
internal static class TrayInteraction
{
    // 押したら必ず止める窓（隠れているアイコンの小窓など。中身はほぼ通知領域のアイコンだけ）。
    private static readonly string[] OverflowClasses =
        ["TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow", "Xaml_WindowedPopupClass"];
    // 押した位置を調べてから決める窓（タスクバー。アプリのボタンや時計もここにある）。
    private static readonly string[] TaskbarClasses = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"];
    private const int Pending = 0, OnIcon = 1, Elsewhere = 2;
    private static long busyUntil;
    private static bool wasPressed;
    private static int press, verdict = Elsewhere;   // verdict は調べ終わった押し始めの結果（別スレッドから書く）

    // アイコンの上で押している間と、離してから 0.6 秒は true。UI スレッドのタイマーから呼ぶ。
    public static bool Busy
    {
        get
        {
            long now = Environment.TickCount64;
            try
            {
                bool pressed = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
                if (pressed && !wasPressed && GetCursorPos(out var point)) Start(point);
                wasPressed = pressed;
                int v = Volatile.Read(ref verdict);
                if (pressed && v == OnIcon) busyUntil = now + 600;
                // 調べている途中（数十 ms）は止めるが、離した後の 0.6 秒は、アイコンの上だと分かったときだけ。
                if (pressed && v == Pending) return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            return now < busyUntil;
        }
    }

    // 押し始め：どの窓の上かを見て、タスクバーなら UI Automation で、通知領域のアイコンの上かを別スレッドで調べる。
    private static void Start(Point point)
    {
        int id = ++press;
        nint window = WindowFromPoint(point), root = window == 0 ? 0 : GetAncestor(window, 2);   // GA_ROOT
        if (root == 0) root = window;
        string? name = ClassName(root);
        if (name is not null && OverflowClasses.Contains(name)) { Volatile.Write(ref verdict, OnIcon); return; }
        if (name is null || !TaskbarClasses.Contains(name)) { Volatile.Write(ref verdict, Elsewhere); return; }
        Volatile.Write(ref verdict, Pending);
        Task.Run(() =>
        {
            int result = IsTrayIcon(point, root) ? OnIcon : Elsewhere;
            if (Volatile.Read(ref press) == id) Volatile.Write(ref verdict, result);
        });
    }

    // タスクバーの中の、通知領域のアイコン（AutomationId が "NotifyItemIcon"。Windows 11 で確認）の枠と、押した点を比べる。
    // 「この点にあるもの」を聞く方法（AutomationElement.FromPoint）では、タスクバーの中までは分からなかった（11/20 実機）。
    // 一覧を取るのは 12〜30ms。調べられなかったときは、以前と同じく止める側に倒す。
    private static bool IsTrayIcon(Point point, nint taskbar)
    {
        try
        {
            var p = new System.Windows.Point(point.X, point.Y);
            var icons = AutomationElement.FromHandle(taskbar).FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon"));
            foreach (AutomationElement icon in icons)
                if (icon.Current.BoundingRectangle.Contains(p)) return true;
            return false;
        }
        catch (Exception) { return true; }
    }

    private static string? ClassName(nint window)
    {
        if (window == 0) return null;
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) == 0 ? null : name.ToString();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
}
