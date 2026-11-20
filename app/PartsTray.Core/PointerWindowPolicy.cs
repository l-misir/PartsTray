namespace PartsTray.Core;

/// <summary>Native operations are injected so HWND routing can be tested without any desktop input.</summary>
public interface IPointerWindows
{
    long WindowAt(PixelPoint point);
    long Parent(long window);
    long Root(long window);
    string ClassName(long window);
    long Style(long window);
    int NonClientHit(long window, PixelPoint point);
    long Foreground { get; }
    uint ThreadOf(long window);
    uint CurrentThread { get; }
    bool TryFocusOf(uint thread, out long focus);
    bool IsChild(long parent, long child);
    bool Enabled(long window);
    bool Visible(long window);
    bool Attach(uint from, uint to, bool attach);
    bool Focus(long window);
    int LastError { get; }
}

public sealed class PointerWindowPolicy(IPointerWindows windows, ILog log)
{
    private string? lastFailure;
    public bool IsVerticalScrollBar(PixelPoint point)
    {
        long child = windows.WindowAt(point);
        if (child == 0) return false;
        long style = windows.Style(child);
        if (windows.ClassName(child) == "ScrollBar" && (style & 1) != 0) return true;
        return (style & 0x00200000) != 0 && windows.NonClientHit(child, point) == 7;
    }

    // Called only by the single focus worker. It never uses UI Automation or activates a root.
    public void FocusUnderCursor(PixelPoint point, Func<bool> allowed)
    {
        if (!allowed()) return;
        long child = windows.WindowAt(point);
        if (child == 0) return;
        if (windows.ClassName(child) is "ScrollBar" or "SysHeader32") child = windows.Parent(child);
        if (child == 0) return;
        long root = windows.Root(child);
        if (root == 0 || child == root || root != windows.Foreground) return;
        uint targetThread = windows.ThreadOf(root);
        if (targetThread == 0) { Failure("GetWindowThreadProcessId"); return; }
        if (!windows.TryFocusOf(targetThread, out long focused)) { Failure("GetGUIThreadInfo"); return; }
        if (focused == child || focused != 0 && windows.IsChild(child, focused)) return;
        if (!windows.Enabled(child) || !windows.Visible(child) || !allowed() || root != windows.Foreground) return;
        uint self = windows.CurrentThread;
        bool attached = false, success = false;
        try
        {
            if (self != targetThread)
            {
                attached = windows.Attach(self, targetThread, true);
                if (!attached) { Failure("AttachThreadInput"); return; }
            }
            // Recheck after attachment: the user may have switched windows while we waited.
            if (!allowed() || root != windows.Foreground || !windows.Enabled(child) || !windows.Visible(child)) return;
            success = windows.Focus(child);
            if (!success) Failure("SetFocus");
        }
        finally
        {
            if (attached && !windows.Attach(self, targetThread, false)) { Failure("AttachThreadInput detach"); success = false; }
            if (success) lastFailure = null;
        }
    }
    private void Failure(string operation)
    {
        string failure = $"HWND フォーカス移動失敗: {operation} error={windows.LastError}";
        if (failure != lastFailure) { lastFailure = failure; log.Write(failure); }
    }
}
