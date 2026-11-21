// カーソル下の窓と UI オートメーション要素を調べ、ジェスチャーに必要な Windows 操作を行います。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class WindowsGestureServices : IGestureHitTest, IGestureInput, IDisposable
{
    private readonly ILog log;
    private readonly Func<AppSettings> readSettings;
    private readonly IClock clock;
    private readonly ScrollProbeLog probeLog;
    private readonly SingleFlightWorker scrollBarLane = new("PartsTray scroll bar UI Automation");
    private readonly ScrollBarAutomation scrollBarAutomation = new();
    private readonly ScrollBarAutomation edgeScrollBarAutomation = new();
    private bool reportedAutomationScrollBar;
    private readonly SingleFlightWorker selectionLane;
    private readonly SingleFlightWorker focusWorker;
    private readonly PointerWindowPolicy pointerWindows;
    public WindowsGestureServices(ILog log, Func<AppSettings> readSettings, IClock clock)
    {
        this.log = log;
        this.readSettings = readSettings;
        this.clock = clock;
        probeLog = new(log);
        pointerWindows = new(new NativePointerWindows(), log);
        selectionLane = new("PartsTray selection UI Automation", ex => log.Write($"UIA 暖機失敗: {ex.Message}"));
        selectionLane.TryPost(() => { _ = AutomationElement.RootElement; });
        focusWorker = new("PartsTray HWND focus", ex => log.Write($"HWND フォーカス処理例外: {ex.Message}"));
    }
    public static nint Root(PixelPoint p) => N.GetAncestor(N.WindowFromPoint(new(p.X, p.Y)), 2);
    public static string Executable(nint window)
    {
        N.GetWindowThreadProcessId(window, out uint pid);
        nint process = N.OpenProcess(0x1000, false, pid);
        if (process == 0) return "";
        try { var b = new StringBuilder(32768); uint size = (uint)b.Capacity; return N.QueryFullProcessImageNameW(process, 0, b, ref size) ? b.ToString() : ""; }
        finally { N.CloseHandle(process); }
    }
    private static int NonClient(nint window, PixelPoint p, uint timeoutMilliseconds = PointerWindowPolicy.NonClientTimeoutMilliseconds)
    {
        nint packed = unchecked((nint)((p.Y & 0xffff) << 16 | p.X & 0xffff));
        return N.SendMessageTimeoutW(window, 0x84, 0, packed, 2, timeoutMilliseconds, out nuint result) != 0 ? unchecked((int)result) : 0;
    }
    public HitResult HitTest(PointerEvent e, AppSettings settings)
    {
        nint root = Root(e.Position);
        if (root == 0) return new(0, false);
        int hit = NonClient(root, e.Position);
        string? command = null;
        if (hit == 2)
        {
            if (e.Button == PointerButton.Middle) command = settings.TitleBar.MiddleClick;
            else if (e.Button == PointerButton.Left)
            {
                // 要確認: Shift+Ctrl の同時押下は Shift の割り当てを優先する。
                command = e.Modifiers.HasFlag(PointerModifiers.Shift) ? settings.TitleBar.ShiftClick : settings.TitleBar.CtrlClick;
            }
        }
        if (hit == 8 && e.Button == PointerButton.Right) command = settings.TitleBar.MinimizeButtonRightClick;
        if (command is not (null or "None")) return new(root, false, command);
        string process = RememberProcess(root)?.Name ?? "";
        if (settings.Gesture.ExcludedProcesses.Contains(process, StringComparer.OrdinalIgnoreCase)) return new(root, false);
        if (e.Button == PointerButton.Right && settings.Gesture.ExplorerSkipSelectedItems && process.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
        {
            bool selected = selectionLane.RunWithTimeout(_ => IsSelectedItem(e.Position), 50, false);
            if (selected) return new(root, false);
        }
        return new(root, true);
    }
    private static bool IsSelectedItem(PixelPoint point)
    {
        var p = new System.Windows.Point(point.X, point.Y);
        var element = AutomationElement.FromPoint(p);
        var leaf = element;
        for (int depth = 0; element != null && depth < 12; depth++, element = TreeWalker.ControlViewWalker.GetParent(element))
        {
            if (element.Current.ControlType != ControlType.ListItem && element.Current.ControlType != ControlType.DataItem) continue;
            if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var value) || !((SelectionItemPattern)value).Current.IsSelected) return false;
            // Do not treat the full-width selected row rectangle as label content.
            var children = element.FindAll(TreeScope.Descendants,
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image)));
            foreach (AutomationElement child in children)
                if (child.Current.BoundingRectangle.Contains(p)) return true;
            if (leaf != element && leaf != null && leaf.Current.ControlType == ControlType.Text) return true;
            // 要確認: UIA が文字/アイコン領域を公開しない項目は余白とみなし、ジェスチャーを許可する。
            return false;
        }
        return false;
    }
    public bool Replay(PointerEvent e)
    {
        uint flags = e.Kind switch
        {
            MouseEventKind.Wheel => 0x800,
            MouseEventKind.HorizontalWheel => 0x1000,
            MouseEventKind.Down => e.Button switch { PointerButton.Left => 2u, PointerButton.Right => 8u, _ => 0x20u },
            MouseEventKind.Up => e.Button switch { PointerButton.Left => 4u, PointerButton.Right => 0x10u, _ => 0x40u },
            _ => 0
        };
        if (flags == 0) return false;
        var input = new N.Input { Data = new() { Mouse = new() {
            Flags = flags, Data = unchecked((uint)e.Delta), Extra = KeyStroke.ExtraInfo } } };
        bool sent = N.SendInput(1, [input], Marshal.SizeOf<N.Input>()) == 1;
        if (!sent) log.Write($"SendInput 拒否: {e.Kind}/{e.Button} error={Marshal.GetLastWin32Error()}");
        return sent;
    }
    public void RequestFocus(PixelPoint p, Func<bool> enabled)
        => focusWorker.TryPost(() =>
        {
            // AttachThreadInput requires both threads to have a message queue.
            N.PeekMessageW(out _, 0, 0, 0, 0);
            pointerWindows.FocusUnderCursor(p, enabled);
        });
    public static bool Foreground(nint window)
    {
        if (!N.IsWindow(window)) return false;
        if (N.GetForegroundWindow() == window) return true;
        N.SetForegroundWindow(window);
        if (N.GetForegroundWindow() == window) return true;
        uint self = N.GetCurrentThreadId(), target = N.GetWindowThreadProcessId(window, out _);
        uint foreground = N.GetWindowThreadProcessId(N.GetForegroundWindow(), out _);
        bool a = false, b = false;
        try
        {
            if (foreground != 0 && foreground != self) a = N.AttachThreadInput(self, foreground, true);
            if (target != 0 && target != self && target != foreground) b = N.AttachThreadInput(self, target, true);
            N.SetForegroundWindow(window);
            return N.GetForegroundWindow() == window;
        }
        finally
        {
            if (b) N.AttachThreadInput(self, target, false);
            if (a) N.AttachThreadInput(self, foreground, false);
        }
    }
    public void Execute(long handle, string command, GestureSettings settings, Func<bool> allowed)
    {
        if (!allowed()) return;
        nint window = (nint)handle;
        if (!N.IsWindow(window)) { log.Write("コマンド対象ウィンドウは既にありません"); return; }
        if (command == "ToggleTopMost")
        {
            bool top = (N.GetWindowLongPtrW(window, -20).ToInt64() & 8) != 0;
            Check(N.SetWindowPos(window, top ? -2 : -1, 0, 0, 0, 0, 0x13), command); return;
        }
        if (command == "OpenExecutableFolder")
        {
            string path = Executable(window);
            if (path.Length == 0) { log.Write("実行ファイルのパスを取得できません"); return; }
            if (!allowed()) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); return;
        }
        if (command == "Minimize") { Check(N.PostMessageW(window, 0x112, 0xF020, 0), command); return; }
        bool tab = false;
        if (command == "Close")
        {
            string process = Path.GetFileName(Executable(window));
            tab = process.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) || settings.BrowserProcesses.Contains(process, StringComparer.OrdinalIgnoreCase);
        }
        if (command == "CloseWindow" || command == "Close" && !tab)
        { Check(N.PostMessageW(window, 0x112, 0xF060, 0), command); return; }
        if (!Foreground(window)) { log.Write("前面化が拒否されたためキー送信を中止"); return; }
        KeyStroke[] keys = command switch
        {
            "Back" => [new(0x12, 0), new(0x25, 1), new(0x25, 3), new(0x12, 2)],
            "Forward" => [new(0x12, 0), new(0x27, 1), new(0x27, 3), new(0x12, 2)],
            "Refresh" => [new(0x74, 0), new(0x74, 2)],
            "Close" => [new(0x11, 0), new(0x57, 0), new(0x57, 2), new(0x11, 2)],
            _ => []
        };
        var inputs = keys.Select(k => new N.Input { Type = 1, Data = new() { Keyboard = new() { Vk = k.VirtualKey, Flags = k.Flags, Extra = KeyStroke.ExtraInfo } } }).ToArray();
        if (inputs.Length == 0 || !allowed() || N.GetForegroundWindow() != window) return;
        uint sent = N.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<N.Input>());
        if (sent != inputs.Length)
        {
            log.Write($"ジェスチャーキー送信失敗 {sent}/{inputs.Length}");
            // Release any partially submitted chord so a modifier cannot be left down.
            if (sent != 0)
            {
                var releases = inputs.Take((int)sent).Where(i => (i.Data.Keyboard.Flags & 2) == 0).Reverse().ToArray();
                for (int i = 0; i < releases.Length; i++) releases[i].Data.Keyboard.Flags |= 2;
                N.SendInput((uint)releases.Length, releases, Marshal.SizeOf<N.Input>());
            }
        }
    }
    private void Check(bool result, string action) { if (!result) log.Write($"{action} 拒否 error={Marshal.GetLastWin32Error()}"); }
    public void Dispose() { selectionLane.Dispose(); scrollBarLane.Dispose(); focusWorker.Dispose(); }

    private sealed class NativePointerWindows : IPointerWindows
    {
        public long WindowAt(PixelPoint p) => N.WindowFromPoint(new(p.X, p.Y));
        public long Parent(long window) => N.GetParent((nint)window);
        public long Root(long window) => N.GetAncestor((nint)window, 2);
        public string ClassName(long window)
        {
            var name = new StringBuilder(256); N.GetClassNameW((nint)window, name, name.Capacity); return name.ToString();
        }
        public long Style(long window) => N.GetWindowLongPtrW((nint)window, -16).ToInt64();
        public bool TryGetClientBounds(long window, out ScrollBarBounds bounds)
        {
            bounds = default;
            if (!N.GetClientRect((nint)window, out var rect)) return false;
            // 左上と右下を一緒に画面座標に直す。移動量が 0 の場合も変換は成功する。
            if (N.MapWindowPoints((nint)window, 0, ref rect, 2) == 0 && Marshal.GetLastPInvokeError() != 0)
                return false;
            bounds = new(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
        }
        public int ScrollBarWidthAt(PixelPoint point) => N.ScrollBarWidthAt(new(point.X, point.Y));
        public int NonClientHit(long window, PixelPoint p, uint timeoutMilliseconds) => NonClient((nint)window, p, timeoutMilliseconds);
        public long Foreground => N.GetForegroundWindow();
        public uint ThreadOf(long window) => N.GetWindowThreadProcessId((nint)window, out _);
        public uint CurrentThread => N.GetCurrentThreadId();
        public bool TryFocusOf(uint thread, out long focus)
        {
            var info = new N.GuiThreadInfo { Size = (uint)Marshal.SizeOf<N.GuiThreadInfo>() };
            bool ok = N.GetGUIThreadInfo(thread, ref info); focus = info.Focus; return ok;
        }
        public bool IsChild(long parent, long child) => N.IsChild((nint)parent, (nint)child);
        public bool Enabled(long window) => N.IsWindowEnabled((nint)window);
        public bool Visible(long window) => N.IsWindowVisible((nint)window);
        public bool Attach(uint from, uint to, bool attach) => N.AttachThreadInput(from, to, attach);
        public bool Focus(long window)
        {
            N.SetFocus((nint)window);
            // SetFocus returns the PREVIOUS focus, which can legitimately be NULL on success.
            return N.GetFocus() == (nint)window;
        }
        public int LastError => Marshal.GetLastPInvokeError();
    }
}
