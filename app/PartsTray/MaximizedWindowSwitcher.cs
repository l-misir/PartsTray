// 最大化ウィンドウの候補収集・前後の切り替え・前面化を共通で行います。
// キーと右ボタン＋ホイールの切り替え方を変えるときに開きます。
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal sealed class MaximizedWindowSwitcher : IDisposable
{
    private readonly ILog log;
    private readonly bool isAdministrator = ProcessElevation.IsAdministrator;
    private readonly System.Windows.Forms.Timer foregroundTimer = new() { Interval = 200 };
    // キー側と右ホイール側を交互に使っても、確認するのは最後の一件だけ。UI スレッド専用。
    private static MaximizedWindowSwitcher? confirming;
    private nint pendingTarget;
    private bool retried, foregroundFailureLogged;
    private Func<bool>? pendingAllowed;

    // 作成・切り替え・破棄はすべて UI スレッドで行う。
    public MaximizedWindowSwitcher(ILog log)
    {
        this.log = log;
        foregroundTimer.Tick += (_, _) => CheckForeground();
    }

    public void BeginSession()
    {
        CancelConfirmation();
        foregroundFailureLogged = false;
    }

    private void CancelConfirmation()
    {
        foregroundTimer.Stop();
        pendingTarget = 0;
        pendingAllowed = null;
        if (ReferenceEquals(confirming, this)) confirming = null;
    }

    public void Dispose()
    {
        CancelConfirmation();
        foregroundTimer.Dispose();
    }

    // 前面のウィンドウを最大化する。最大化していれば元の大きさに戻す。デスクトップやタスクバーには何もしない。
    public static void ToggleMaximize()
    {
        nint window = Root(GetForegroundWindow());
        if (window == 0 || IsShell(window)) return;
        ShowWindow(window, IsZoomed(window) ? 9 : 3);   // SW_RESTORE : SW_MAXIMIZE
    }

    // カーソルのあるモニターの窓を重なり順に集め、Core で選んだ操作を実行する。
    public void Switch(bool previous, Func<bool>? allowed = null)
    {
        confirming?.CancelConfirmation();
        CancelConfirmation(); // 新しい依頼が空振りでも、以前の窓を後から前面化しない。
        if (allowed?.Invoke() == false || !GetCursorPos(out var point)) return;
        nint monitor = MonitorFromPoint(point, 2);
        nint foreground = Root(GetForegroundWindow());
        var windows = new List<WindowSwitchWindow>();
        EnumWindows((window, _) => { CollectWindow(window, windows); return true; }, 0);
        var plan = WindowSwitchPolicy.Select(windows, monitor, foreground, previous, isAdministrator);
        if (plan.Target == 0) return;
        if (plan.SendToBottom != 0)
            SetWindowPos((nint)plan.SendToBottom, 1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_BOTTOM
        if (plan.Restore) ShowWindow((nint)plan.Target, 9); // SW_RESTORE（最大化へ復元）
        SetForegroundWindow((nint)plan.Target);
        pendingTarget = (nint)plan.Target;
        pendingAllowed = allowed;
        retried = false;
        confirming = this;
        foregroundTimer.Start();
    }

    private void CollectWindow(nint window, List<WindowSwitchWindow> windows)
    {
        if (!IsWindowVisible(window) || GetWindow(window, 4) != 0) return; // GW_OWNER
        if ((GetWindowLongPtr(window, -20) & 0x80) != 0 || IsShell(window)) return; // WS_EX_TOOLWINDOW
        if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return;
        bool minimized = IsIconic(window);
        uint flags = 0;
        nint monitor;
        if (minimized)
        {
            var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(window, ref placement)) return;
            flags = placement.Flags;
            // rcNormalPosition は主モニターの作業領域を原点とする座標。画面座標にしてから調べる。
            var primary = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromPoint(default, 1), ref primary)) return;
            var rect = placement.NormalPosition;
            rect.Left += primary.Work.Left; rect.Right += primary.Work.Left;
            rect.Top += primary.Work.Top; rect.Bottom += primary.Work.Top;
            monitor = MonitorFromRect(ref rect, 2);
        }
        else monitor = MonitorFromWindow(window, 2);
        windows.Add(new(window, monitor, minimized, IsZoomed(window), flags,
            IsElevated: !isAdministrator && ProcessElevation.IsWindowElevated(window)));
    }

    private static nint Root(nint window) => window == 0 ? 0 : GetAncestor(window, 2);   // GA_ROOT
    private static bool IsShell(nint window)
    {
        var name = new System.Text.StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private void CheckForeground()
    {
        foregroundTimer.Stop();
        nint target = pendingTarget;
        try
        {
            if (target == 0 || pendingAllowed?.Invoke() == false || !IsWindow(target) ||
                Root(GetForegroundWindow()) == target)
            {
                CancelConfirmation();
                return;
            }
            if (!retried)
            {
                // 移動もボタンもない入力で一度だけ再試行する。Alt は送らない。
                var neutral = new GestureNative.Input
                {
                    Type = 0,
                    Data = new GestureNative.InputUnion { Mouse = new GestureNative.Mouse { Extra = KeyStroke.ExtraInfo } }
                };
                GestureNative.SendInput(1, [neutral], Marshal.SizeOf<GestureNative.Input>());
                SetForegroundWindow(target);
                retried = true;
                foregroundTimer.Start(); // 再試行の結果も 200 ms 待ってから確認。
                return;
            }
            CancelConfirmation();
            if (foregroundFailureLogged) return;
            foregroundFailureLogged = true;
            log.Write("最大化ウィンドウを前面にできませんでした（同じ選択時間・右ボタン押下では一度だけ記録）");
        }
        catch (Exception ex)
        {
            CancelConfirmation();
            if (foregroundFailureLogged) return;
            foregroundFailureLogged = true;
            log.Write($"最大化ウィンドウの前面確認に失敗: {ex.Message}");
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPlacement
    {
        public uint Length, Flags, ShowCommand;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
    }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Rect rect, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate bool EnumWindowsProc(nint window, nint param);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, nint param);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
