// 新しく表示された窓のプロセス名を、最初のホイールより前に判定スレッドで調べます。
using System.Runtime.InteropServices;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class GestureModule
{
    private delegate void WinEventProc(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc callback,
        uint process, uint thread, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);
    private readonly WinEventProc windowShown;
    private nint windowShowHook, windowLocationHook;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, byte> changedWindows = new();
    private int scanWheelWindows, rebuildRenderers;
    private readonly ManualResetEventSlim firstWindowScan = new();

    public EdgeWheelTarget FirstChromiumEdge(PixelPoint point, WheelSettings wheel) => services.FirstChromiumEdge(point, wheel);
    public bool IsCurrentWheelWindow(WheelWindow window) => WindowsGestureServices.IsCurrentWheelWindow(window);

    private void WatchWheelWindows(WheelSettings wheel)
    {
        if (wheel.FastScrollOnScrollBar || wheel.EdgeRedirect)
        {
            if (windowShowHook == 0)
                windowShowHook = SetWinEventHook(0x8002, 0x8002, 0, windowShown, 0, 0, 0); // EVENT_OBJECT_SHOW
            bool prepareEdges = wheel.FastScrollOnScrollBar && wheel.EdgeAssist;
            if (prepareEdges) Interlocked.Exchange(ref rebuildRenderers, 1);
            if (prepareEdges && windowLocationHook == 0)
            {
                windowLocationHook = SetWinEventHook(0x800B, 0x800B, 0, windowShown, 0, 0, 0); // EVENT_OBJECT_LOCATIONCHANGE
            }
            if (!prepareEdges && windowLocationHook != 0)
            { UnhookWinEvent(windowLocationHook); windowLocationHook = 0; }
            RequestWindowScan();
        }
        else StopWatchingWheelWindows();
    }

    private void StopWatchingWheelWindows()
    {
        if (windowShowHook != 0) UnhookWinEvent(windowShowHook);
        if (windowLocationHook != 0) UnhookWinEvent(windowLocationHook);
        windowShowHook = windowLocationHook = 0;
    }

    private void WindowShown(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != 0 || childId != 0 || window == 0) return;
        if (kind == 0x8002 && N.GetAncestor(window, 2) == window) RequestWindowScan();
        // 新しい子窓の表示と、既知のレンダラー（または親窓）の移動・サイズ変更だけを渡す。
        if (windowLocationHook != 0 && (kind == 0x8002 || services.TracksChromiumGeometry(window)) &&
            changedWindows.TryAdd(window, 0))
            decisionWake.Set();
    }

    private void RequestWindowScan()
    {
        if (Interlocked.Exchange(ref scanWheelWindows, 1) == 0) decisionWake.Set();
    }

    private void PrepareWheelWindows()
    {
        try
        {
            if (Interlocked.Exchange(ref scanWheelWindows, 0) != 0)
                services.PrepareWheelWindows(Interlocked.Exchange(ref rebuildRenderers, 0) != 0);
            foreach (var pair in changedWindows)
                if (changedWindows.TryRemove(pair.Key, out _)) services.RefreshChromiumGeometry(pair.Key);
        }
        catch (Exception ex) { log.Write($"ホイール除外用の窓の確認: {ex.Message}"); }
    }
}
