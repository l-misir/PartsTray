// 高速スクロールの判定と画面端の補助を判定スレッドで実行します。
using System.Diagnostics;
using System.Runtime.InteropServices;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class WindowsGestureServices
{
    public PointerCache Probe(PixelPoint p, TimeSpan at)
    {
        var settings = readSettings();
        var wheel = settings.Wheel;
        var details = settings.Diagnostics.ScrollProbe ? new ScrollProbeDetails() : null;
        bool inspectWindows = details != null || wheel.EdgeRedirect || wheel.FastScrollOnScrollBar;
        var (cursor, monitor, bounds) = inspectWindows ? EdgeWheelWindows.NearestMonitor(p) : (p, (nint)0, Rectangle.Empty);
        nint source = inspectWindows ? N.WindowFromPoint(new(cursor.X, cursor.Y)) : 0;
        if (details != null) details.SourceClass = ScrollProbeDetails.WindowClass(source);
        nint root = source != 0 ? N.GetAncestor(source, 2) : 0;
        var process = root != 0 ? RememberProcess(root) : null;
        var window = new WheelWindow(root, process?.Id ?? 0, cursor, source);
        if (inspectWindows && !wheel.AllowsFastScroll(process?.Name))
        {
            var (dragWindow, dragOrigin) = DisabledTitleBarAt(p);
            if (details != null)
            {
                details.Monitor = bounds;
                details.Write(probeLog, p, wheel.EdgeAssist, process is { Name.Length: > 0 }
                    ? "Excluded" : "UnknownProcess", (long)(clock.Now - at).TotalMilliseconds, cursor);
            }
            return new(p, clock.Now, false, dragWindow, dragOrigin, WheelWindow: window, WheelExcluded: true);
        }
        var edge = details != null || wheel.EdgeRedirect || wheel.FastScrollOnScrollBar && wheel.EdgeAssist
            ? EdgeWheelWindows.At(cursor, source, monitor, bounds, details) : null;
        var points = new ScrollBarProbe(cursor, wheel.EdgeAssist ? edge?.Inside : null, p);
        var (foundAt, result, renderer) = wheel.FastScrollOnScrollBar
            ? ProbeScrollBars(points, source, edge?.Target ?? 0, details) : (null, "FastScrollOff", 0L);
        var (disabled, origin) = DisabledTitleBarAt(p);

        // 遅い問い合わせの間に窓が変わった場合は、その結果を新しい窓に渡さない。
        var redirect = wheel.EdgeRedirect && edge is { } candidate ? candidate.RedirectTarget() : default;
        var chromiumEdge = renderer != 0 && edge is { } frame ? frame.ChromiumTarget(points, foundAt, renderer) : default;
        bool currentEdge = edge is { } pair && pair.StillAtWindows();
        if (!currentEdge)
        {
            redirect = default;
            chromiumEdge = default;
        }
        if (foundAt.HasValue && (foundAt == cursor ? N.WindowFromPoint(new(cursor.X, cursor.Y)) != source : !currentEdge))
        {
            foundAt = null;
            chromiumEdge = default;
            result = "WindowChanged";
        }
        if (foundAt.HasValue && renderer != 0 && wheel.EdgeAssist)
            RememberRenderer((nint)renderer);
        if (foundAt.HasValue && result == "UIA" && !reportedAutomationScrollBar)
        {
            reportedAutomationScrollBar = true;
            log.Write("スクロールバー（UI オートメーション）を見分けました");
        }
        var cache = points.ToPointerCache(clock, foundAt, disabled, origin, redirect, chromiumEdge) with { WheelWindow = window };
        if (details != null)
        {
            if (foundAt.HasValue) result += foundAt == cursor ? "@cursor" : "@inside";
            details.Write(probeLog, p, wheel.EdgeAssist, result, (long)(cache.At - at).TotalMilliseconds, cursor);
        }
        return cache;
    }

    private sealed record UiaProgress(string Point, AutomationPointResult? Direct);

    private (PixelPoint? Point, string Result, long Renderer) ProbeScrollBars(ScrollBarProbe points, nint source, nint target, ScrollProbeDetails? details)
    {
        long start = Stopwatch.GetTimestamp();
        int Remaining() => (int)Math.Max(0, PointerWindowPolicy.NonClientTimeoutMilliseconds - Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        var (direct, inward) = points.CheckNative(pointerWindows, Remaining);
        if (details != null)
        {
            details.Native = direct.Result;
            details.NativeInside = inward?.Result ?? "-";
            details.UsedInside = inward.HasValue;
        }
        if (direct.Hit) return (points.Cursor, direct.Result, direct.ChromiumWindow);
        if (inward is { Hit: true } match) return (points.Inside, match.Result, match.ChromiumWindow);

        // 呼び出し側が時間切れになっても、専用スレッドでは元・内側の調査を終えて各点の結果を保存する。
        bool diagnostics = details != null;
        UiaProgress? progress = null;
        var wait = scrollBarLane.RunForCache(active =>
        {
            if (diagnostics) Volatile.Write(ref progress, new("cursor", null));
            var cursor = scrollBarAutomation.Inspect(points.Cursor, source, active, diagnostics);
            AutomationPointResult? inside = null;
            if (!cursor.Hit && active() && points.Inside is { } p)
            {
                if (diagnostics) Volatile.Write(ref progress, new("inside", cursor));
                inside = edgeScrollBarAutomation.Inspect(p, target, active, diagnostics);
            }
            return (Cursor: cursor, Inside: inside);
        }, ScrollBarPolicy.TimeoutMilliseconds);
        if (details != null)
        {
            details.Uia = wait.Status.ToString();
            details.WaitMs = (long)wait.Elapsed.TotalMilliseconds;
            if (wait.Status == WorkerWaitStatus.Timeout)
            {
                var pending = Volatile.Read(ref progress);
                details.WaitingAt = pending?.Point ?? "queued";
                details.DirectUia = pending?.Direct;
            }
        }
        if (wait.Status != WorkerWaitStatus.Completed) return (null, wait.Status.ToString(), 0);
        var uia = wait.Result;
        if (details != null) (details.DirectUia, details.InsideUia) = uia;
        if (uia.Cursor.Hit) return (points.Cursor, "UIA", 0);
        if (uia.Inside is { Hit: true }) return (points.Inside, "UIA", 0);
        return (null, "Miss", 0);
    }

    // このログの状態も判定スレッドが単独で所有する。静止後・設定変更・終了時に端数を出す。
    public void FlushProbeLog(bool stopping = false)
    {
        if (stopping || !readSettings().Diagnostics.ScrollProbe) probeLog.Reset();
        else probeLog.FlushDue(Environment.TickCount64);
    }

    private static (long Window, PixelPoint Origin) DisabledTitleBarAt(PixelPoint p)
    {
        long disabled = 0; PixelPoint origin = default;
        N.EnumWindows((window, _) =>
        {
            if (!N.IsWindowVisible(window) || N.IsIconic(window) || !N.GetWindowRect(window, out var rect) || !rect.Contains(p.X, p.Y)) return true;
            if (N.DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            // Ignore our click-through overlays when walking physical Z order.
            if ((N.GetWindowLongPtrW(window, -20).ToInt64() & 0x20) != 0) return true;
            if (!N.IsWindowEnabled(window))
            {
                var info = new N.TitleBarInfo { Size = (uint)Marshal.SizeOf<N.TitleBarInfo>() };
                if (N.GetTitleBarInfo(window, ref info) && info.Rect.Contains(p.X, p.Y))
                { disabled = window; origin = new(rect.Left, rect.Top); }
            }
            return false; // An enabled/occluding window above it must not be dragged through.
        }, 0);
        return (disabled, origin);
    }
}
