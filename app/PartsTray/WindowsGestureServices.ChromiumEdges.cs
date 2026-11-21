// 画面と Chromium の表示領域を判定スレッドで先読みし、最初のホイールも内側へ届けます。
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class WindowsGestureServices
{
    private readonly record struct EdgeMonitor(nint Handle, Rectangle Bounds, uint Dpi, int ScrollBarWidth);
    private sealed record Renderer(nint Root, uint Process, nint ClassAtom, ScrollBarBounds Bounds);
    private EdgeMonitor[] edgeMonitors = [];
    private readonly ConcurrentDictionary<nint, Renderer> renderers = new();
    private readonly ConcurrentDictionary<nint, byte> rendererRoots = new();
    private readonly Dictionary<nint, uint> scannedRoots = new();
    private readonly ConcurrentDictionary<nint, (nint Root, uint Process)> postableChildren = new();
    private readonly NativePointerWindows rendererWindows = new();

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, N.EnumWindowProc callback, nint data);

    private void PrepareChromiumMonitors()
    {
        var monitors = new List<EdgeMonitor>();
        foreach (var screen in Screen.AllScreens)
        {
            var p = new PixelPoint(screen.Bounds.Left, screen.Bounds.Top);
            var (point, monitor, bounds) = EdgeWheelWindows.NearestMonitor(p);
            if (monitor != 0 && N.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0)
                monitors.Add(new(monitor, bounds, dpi, N.ScrollBarWidthAt(new(point.X, point.Y))));
        }
        Volatile.Write(ref edgeMonitors, monitors.ToArray());
    }

    private void PrepareChromiumRoot(nint root, uint pid)
    {
        if (scannedRoots.TryGetValue(root, out var previous) && pid == previous) return;
        scannedRoots[root] = pid;
        // 子をすべて調べるのは窓が初めて現れた時だけ。後から表示された子は SHOW 通知で補う。
        EnumChildWindows(root, (child, _) => { RememberRenderer(child); return true; }, 0);
    }

    private void ForgetClosedChromiumWindows()
    {
        foreach (var root in scannedRoots.Keys.ToArray())
            if (!N.IsWindow(root)) { scannedRoots.Remove(root); rendererRoots.TryRemove(root, out _); }
        foreach (var pair in renderers)
            if (!N.IsWindow(pair.Key)) renderers.TryRemove(pair.Key, out _);
        foreach (var pair in postableChildren)
            if (!N.IsWindow(pair.Key)) postableChildren.TryRemove(pair.Key, out _);
    }

    public bool TracksChromiumGeometry(nint window) => renderers.ContainsKey(window) || rendererRoots.ContainsKey(window);

    public void RefreshChromiumGeometry(nint window)
    {
        nint root = N.GetAncestor(window, 2);
        if (root == 0 || !readSettings().Wheel.AllowsFastScroll(RememberProcess(root)?.Name)) return;
        if (rendererRoots.ContainsKey(window))
        {
            foreach (var pair in renderers)
                if (pair.Value.Root == window) RememberRenderer(pair.Key);
        }
        else RememberRenderer(window);
    }

    private void RememberRenderer(nint window)
    {
        nint root = N.GetAncestor(window, 2);
        if (root != 0 && rendererWindows.ClassName(window) == "Chrome_RenderWidgetHostHWND" &&
            rendererWindows.TryGetClientBounds(window, out var bounds))
        {
            renderers[window] = new(root, ProcessId(window), N.GetClassLongPtrW(window, -32), bounds); // GCW_ATOM
            if (rendererRoots.TryAdd(root, 0))
            {
                // 親の縁や別の子から届け直す場合に備え、同じルートの子の権限を一度だけ確認する。
                EnumChildWindows(root, (child, _) => { RememberPostablePath(child, root); return true; }, 0);
            }
        }
        else renderers.TryRemove(window, out _);
        if (root != 0 && window != root && rendererRoots.ContainsKey(root)) RememberPostablePath(window, root);
    }

    private void RememberPostablePath(nint window, nint root)
    {
        uint pid = ProcessId(window);
        for (nint current = window; current != 0 && RememberProcess(current) is { CanPost: true };
            current = N.GetAncestor(current, 1)) // GA_PARENT
        {
            if (current != root) continue;
            postableChildren[window] = (root, pid);
            return;
        }
        postableChildren.TryRemove(window, out _);
    }

    private bool IsPostableChild(nint window, nint root) =>
        window == root || postableChildren.TryGetValue(window, out var child) &&
        child.Root == root && child.Process == ProcessId(window);

    public EdgeWheelTarget FirstChromiumEdge(PixelPoint input, WheelSettings wheel)
    {
        nint monitor = N.MonitorFromPoint(new(input.X, input.Y), 2);
        foreach (var screen in Volatile.Read(ref edgeMonitors))
        {
            if (screen.Handle != monitor) continue;
            var point = EdgeWheelPolicy.ClampToMonitor(input, screen.Bounds);
            if (EdgeWheelPolicy.InwardPoint(point, screen.Bounds, screen.Dpi) is not { } inside) return default;
            nint source = N.WindowFromPoint(new(point.X, point.Y)), target = N.WindowFromPoint(new(inside.X, inside.Y));
            if (source == 0 || target == 0) return default;
            renderers.TryGetValue(source, out var direct);
            renderers.TryGetValue(target, out var inward);
            var points = new ScrollBarProbe(point, inside);
            var destination = points.FirstChromiumTarget(source, direct?.Bounds, target, inward?.Bounds, screen.ScrollBarWidth);
            if (destination.Window == 0) return default;
            var renderer = destination.Window == source ? direct! : inward!;
            // 先読み後に前面の窓が変わっていたら、元入力をそのまま通す。除外名・昇格照会はキャッシュのみ。
            if (renderer.ClassAtom == 0 || N.GetClassLongPtrW((nint)destination.Window, -32) != renderer.ClassAtom ||
                N.GetAncestor(source, 2) != renderer.Root || !IsPostableChild(source, renderer.Root) ||
                source != target && N.GetAncestor(target, 2) != renderer.Root ||
                destination.Window != source && !IsPostableChild((nint)destination.Window, renderer.Root) ||
                renderer.Process != ProcessId((nint)destination.Window) || !ChromiumWheelAllowed(renderer.Root, wheel)) return default;
            if (!N.IsZoomed(renderer.Root) && (!N.GetWindowRect(renderer.Root, out var r) ||
                !EdgeWheelPolicy.CanProbe(false, Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), screen.Bounds)))
                return default;
            return destination;
        }
        return default;
    }
}
