// 判定スレッドで画面端のウィンドウを調べ、同じルートに属する内側の子ウィンドウを選びます。
using System.Runtime.InteropServices;
using System.Text;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal readonly record struct EdgeWheelWindows(PixelPoint Position, PixelPoint Inside, nint Root, nint Source, nint Target)
{
    private static readonly bool Administrator = ProcessElevation.IsAdministrator;

    public static (PixelPoint Point, nint Monitor, Rectangle Bounds) NearestMonitor(PixelPoint point)
    {
        nint monitor = N.MonitorFromPoint(new(point.X, point.Y), 2); // MONITOR_DEFAULTTONEAREST
        var info = new N.MonitorInfo { Size = (uint)Marshal.SizeOf<N.MonitorInfo>() };
        if (monitor == 0 || !N.GetMonitorInfoW(monitor, ref info)) return (point, 0, default);
        var bounds = Bounds(info.Monitor);
        if (bounds.Width <= 0 || bounds.Height <= 0) return (point, 0, default);
        return (EdgeWheelPolicy.ClampToMonitor(point, bounds), monitor, bounds);
    }

    public static EdgeWheelWindows? At(PixelPoint point, nint source, nint monitor, Rectangle bounds, ScrollProbeDetails? details)
    {
        EdgeWheelWindows? Skip(string reason)
        {
            if (details != null) details.Geometry = reason;
            return null;
        }
        nint root = N.GetAncestor(source, 2); // GA_ROOT
        bool maximized = root != 0 && N.IsZoomed(root);
        if (details != null && root != 0) details.Maximized = maximized;
        if (monitor == 0) return Skip("NoMonitor");
        if (details != null) details.Monitor = bounds;
        if (N.GetDpiForMonitor(monitor, 0, out uint dpi, out _) != 0 || dpi == 0) return Skip("NoDpi");
        if (EdgeWheelPolicy.InwardPoint(point, bounds, dpi) is not { } inside) return Skip("AwayFromEdge");

        nint target = N.WindowFromPoint(new(inside.X, inside.Y));
        if (details != null)
        {
            details.Inside = inside;
            details.InsideClass = ScrollProbeDetails.WindowClass(target);
        }
        if (root == 0) return Skip("NoRoot");
        bool haveRect = N.GetWindowRect(root, out var rect);
        bool covers = haveRect && EdgeWheelPolicy.CanProbe(false, Bounds(rect), bounds);
        if (details != null && haveRect) details.CoversMonitor = covers;
        if (!maximized && !covers) return Skip(haveRect ? "Windowed" : "NoWindowBounds");
        if (target == 0) return Skip("NoInsideWindow");
        if (N.GetAncestor(target, 2) != root) return Skip("DifferentRoot");
        if (details != null) details.Geometry = "Ready";
        return new(point, inside, root, source, target);
    }

    public bool StillAtWindows() =>
        N.WindowFromPoint(new(Position.X, Position.Y)) == Source &&
        N.WindowFromPoint(new(Inside.X, Inside.Y)) == Target &&
        N.GetAncestor(Source, 2) == Root && N.GetAncestor(Target, 2) == Root;

    public EdgeWheelTarget RedirectTarget()
    {
        // 画面いっぱいに広げただけの窓は高速スクロールの判定のみ。
        bool maximized = N.IsZoomed(Root);
        if (!maximized) return default;
        nint source = DeepestAt(Source, Position), target = DeepestAt(Target, Inside);
        if (source == 0 || target == 0 || target == Root || source == target) return default;

        return CheckedTarget(source, target, maximized, chromiumEdge: false);
    }

    public EdgeWheelTarget ChromiumTarget(ScrollBarProbe points, PixelPoint? foundAt, long renderer)
    {
        var target = points.ChromiumTarget(foundAt, renderer, Source, Target);
        // At で最大化または全画面と確認した窓。通常の届け直しとは違い、元と同じ HWND にも内側の座標で送る。
        return target.Window != 0 ? CheckedTarget(Source, (nint)target.Window, true, chromiumEdge: true) : default;
    }

    private EdgeWheelTarget CheckedTarget(nint source, nint target, bool eligibleWindow, bool chromiumEdge)
    {
        var processes = new Dictionary<uint, nint>();
        bool chromium = false;
        bool sameRoot = ReadPath(source, processes, ref chromium) &&
            (source == target || ReadPath(target, processes, ref chromium));
        bool ownProcess = processes.ContainsKey((uint)Environment.ProcessId);
        bool elevated = sameRoot && chromium == chromiumEdge && !ownProcess && !Administrator &&
            processes.Values.Any(ProcessElevation.IsWindowElevated);
        if (!EdgeWheelPolicy.CanRedirect(eligibleWindow, source, target, sameRoot, chromium, ownProcess, elevated, Administrator, chromiumEdge))
            return default;
        if (N.GetWindowThreadProcessId(target, out uint targetPid) == 0 || !processes.ContainsKey(targetPid)) return default;
        return new(target, Inside, targetPid);
    }

    private bool ReadPath(nint window, Dictionary<uint, nint> processes, ref bool chromium)
    {
        var name = new StringBuilder(256);
        while (window != 0)
        {
            if (N.GetWindowThreadProcessId(window, out uint pid) == 0 || pid == 0 ||
                N.GetClassNameW(window, name, name.Capacity) == 0) return false;
            processes.TryAdd(pid, window);
            chromium |= name.ToString() == "Chrome_RenderWidgetHostHWND";
            if (window == Root) return true;
            window = N.GetAncestor(window, 1); // GA_PARENT（所有者の窓へは移らない）
        }
        return false;
    }

    private static nint DeepestAt(nint window, PixelPoint point)
    {
        while (window != 0)
        {
            var client = new N.Point(point.X, point.Y);
            if (N.MapWindowPoints(0, window, ref client, 1) == 0 && Marshal.GetLastPInvokeError() != 0)
                return 0;
            // 非クライアント領域では子がない。隠れた子、無効な子、透明な子は除外する。
            nint child = N.ChildWindowFromPointEx(window, client, 1 | 2 | 4);
            if (child == 0 || child == window) return window;
            window = child;
        }
        return 0;
    }

    private static Rectangle Bounds(N.Rect rect) => Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
