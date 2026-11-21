// ポインターの下から UI オートメーションの親をたどり、縦スクロールの範囲を判定用スレッドで調べます。
using System.Diagnostics;
using System.Windows.Automation;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal readonly record struct AutomationPointResult(bool Hit, bool Cached, string Reason, long QueryMilliseconds);

internal sealed class ScrollBarAutomation
{
    private readonly ScrollBarCache cache = new();
    // このインスタンスへの問い合わせはすべて同じ UIA スレッドで直列に実行される。
    private nint cachedRoot;
    private WalkDetails cachedWalk = new();
    private long cachedDuration;

    public AutomationPointResult Inspect(PixelPoint point, nint expectedWindow, Func<bool> active, bool diagnostics)
    {
        long start = Stopwatch.GetTimestamp();
        long Elapsed() => (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        AutomationPointResult Miss(string reason) => new(false, false, reason, Elapsed());

        nint window = N.WindowFromPoint(new(point.X, point.Y));
        if (window != expectedWindow) return Miss("window-changed");
        nint root = N.GetAncestor(window, 2); // GA_ROOT
        int width = N.ScrollBarWidthAt(new(point.X, point.Y));
        if (root == 0) return Miss("no-window");
        if (width <= 0) return Miss("no-scrollbar-width");
        if (!active()) return Miss("stopped");
        if (root == cachedRoot && cache.TryAtPoint(window, point, width, Environment.TickCount64, out bool cachedHit))
            return FromCache(cachedHit);

        var walk = new WalkDetails();
        try
        {
            var request = Properties();
            AutomationElement leaf;
            using (request.Activate()) leaf = AutomationElement.FromPoint(new(point.X, point.Y));
            if (leaf == null) return Miss("no-element");
            if (!StillAtWindow()) return Miss("window-changed");

            int[] leafId = leaf.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty, true) as int[] ?? [];
            if (root == cachedRoot && cache.TryForElement(window, leafId, point, width, Environment.TickCount64, out cachedHit))
                return FromCache(cachedHit);

            // 300ms は呼び出し側の待ち時間。遅れて返ったプロバイダーの結果も、次回に使えるよう保存する。
            var path = ScrollBarPolicy.Inspect(point, width, Parents(leaf, root, request, walk), active);
            if (path == null || !active()) return Miss("stopped");
            if (!StillAtWindow()) return Miss("window-changed");
            if (walk.Depth < 0) return Miss(diagnostics ? walk.Progress() : "");
            if (!path.TryClassify(point, width, out bool hit)) return Miss("incomplete-path");
            cache.Remember(window, leafId, point, path, Environment.TickCount64);
            (cachedRoot, cachedWalk, cachedDuration) = (root, walk, Elapsed());
            return new(hit, false, diagnostics ? walk.Reason(hit) : "", cachedDuration);
        }
        catch (Exception ex)
        {
            // 例外メッセージに外部アプリの文字列が混じり得るので、型と探索の進み具合だけを残す。
            return Miss(diagnostics ? $"provider-error={ex.GetType().Name} {walk.Progress()}" : "");
        }

        bool StillAtWindow() => N.WindowFromPoint(new(point.X, point.Y)) == window && N.GetAncestor(window, 2) == root;
        AutomationPointResult FromCache(bool hit) => new(hit, true, diagnostics ? cachedWalk.Reason(hit) : "", cachedDuration);
    }

    // 一段ごとに複数回プロバイダーへ問い合わせず、必要なプロパティをまとめて読む。
    private static CacheRequest Properties()
    {
        var request = new CacheRequest { TreeFilter = Automation.RawViewCondition };
        request.Add(AutomationElement.RuntimeIdProperty);
        request.Add(AutomationElement.NativeWindowHandleProperty);
        request.Add(AutomationElement.BoundingRectangleProperty);
        request.Add(AutomationElement.ControlTypeProperty);
        request.Add(AutomationElement.OrientationProperty);
        request.Add(AutomationElement.IsScrollPatternAvailableProperty);
        // GetCachedPropertyValue は登録したプロパティを直接読む。パターン本体の取得は不要。
        request.Add(ScrollPattern.VerticallyScrollableProperty);
        return request;
    }

    private static IEnumerable<ScrollBarNode> Parents(AutomationElement element, nint root, CacheRequest request, WalkDetails walk)
    {
        for (int depth = 0; element != null; depth++)
        {
            nint handle = element.Cached.NativeWindowHandle;
            if (handle != 0 && handle != root && N.GetAncestor(handle, 2) != root)
            {
                walk.End = "other-window";
                yield break;
            }
            var rect = element.Cached.BoundingRectangle;
            var type = element.Cached.ControlType;
            bool pattern = element.GetCachedPropertyValue(AutomationElement.IsScrollPatternAvailableProperty, true) is true;
            bool scrolls = pattern && element.GetCachedPropertyValue(ScrollPattern.VerticallyScrollableProperty, true) is true;
            bool verticalBar = false;
            if (type == ControlType.ScrollBar)
            {
                var orientation = element.Cached.Orientation;
                walk.ScrollBar ??= (depth, orientation);
                verticalBar = orientation == OrientationType.Vertical;
            }
            if (depth == 0) walk.LeafType = type?.ProgrammaticName ?? "-";
            walk.Depth = depth;
            if (pattern) walk.PatternDepth ??= depth;
            if (scrolls) walk.VerticalDepth ??= depth;
            if (verticalBar) walk.BarDepth ??= depth;
            walk.End = handle == root ? "window-root" : depth == ScrollBarPolicy.MaxParentDepth ? "depth-limit" : "hit";
            yield return new(scrolls, new(rect.Left, rect.Top, rect.Right, rect.Bottom), handle == root, verticalBar);
            // Inspect は、まだヒットせずルートや段数の上限にも届かない場合だけ次を要求する。
            walk.End = "get-parent";
            element = TreeWalker.RawViewWalker.GetParent(element, request);
        }
        walk.End = "no-parent";
    }

    private sealed class WalkDetails
    {
        public string LeafType = "-", End = "from-point";
        public (int Depth, OrientationType Orientation)? ScrollBar;
        public int Depth = -1;
        public int? PatternDepth, VerticalDepth, BarDepth;

        public string Reason(bool hit)
        {
            string result = hit ? "hit" : VerticalDepth.HasValue || BarDepth.HasValue ? "outside-band"
                : PatternDepth.HasValue ? "not-vertically-scrollable" : "no-scroll-pattern";
            return $"{result} {Progress()}";
        }

        public string Progress() => $"leaf={LeafType} depth={Depth} end={End} " +
            $"patternDepth={PatternDepth?.ToString() ?? "-"} verticalDepth={VerticalDepth?.ToString() ?? "-"} " +
            $"scrollBar={(ScrollBar is { } bar ? $"{bar.Depth}/{bar.Orientation}" : "-")} barDepth={BarDepth?.ToString() ?? "-"}";
    }
}
