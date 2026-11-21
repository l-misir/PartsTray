// UI オートメーションで得た矩形から縦スクロールバーの帯を判定し、親をたどる範囲と結果の有効期間を管理します。
namespace PartsTray.Core;

public readonly record struct ScrollBarBounds(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(PixelPoint point) =>
        double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right) && double.IsFinite(Bottom) &&
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool InRightBand(PixelPoint point, int width) =>
        width > 0 && Contains(point) && point.X >= Right - width - 2;
}

public readonly record struct ScrollBarNode(bool VerticallyScrollable, ScrollBarBounds Bounds, bool IsRoot,
    bool IsVerticalBar = false)
{
    public bool Hit(PixelPoint point, int width) =>
        IsVerticalBar && Bounds.Contains(point) || VerticallyScrollable && Bounds.InRightBand(point, width);
}

public sealed class ScrollBarPath
{
    private readonly List<ScrollBarNode> areas;
    private readonly bool complete;

    internal ScrollBarPath(List<ScrollBarNode> areas, bool complete)
        => (this.areas, this.complete) = (areas, complete);

    public bool TryClassify(PixelPoint point, int width, out bool hit)
    {
        foreach (var area in areas)
        {
            if (area.Hit(point, width))
            {
                hit = true;
                return true;
            }
        }
        hit = false;
        // 帯を見つけて途中で止まった経路では、未確認の親に別の帯があるかもしれない。
        return complete;
    }
}

public static class ScrollBarPolicy
{
    public const int TimeoutMilliseconds = 300;
    public const int MaxParentDepth = 32;

    // 列挙器は葉から順に UIA 要素を取得する。ルートや深さの上限で次の親の取得自体を止める。
    public static ScrollBarPath? Inspect(PixelPoint point, int width, IEnumerable<ScrollBarNode> nodes, Func<bool> active)
    {
        var areas = new List<ScrollBarNode>();
        using var walker = nodes.GetEnumerator();
        for (int depth = 0; depth <= MaxParentDepth; depth++)
        {
            if (!active()) return null;
            bool found = walker.MoveNext();
            if (!active()) return null;
            if (!found) break;
            var node = walker.Current;
            if (node.VerticallyScrollable || node.IsVerticalBar) areas.Add(node);
            if (node.IsRoot || depth == MaxParentDepth) break;
            if (node.Hit(point, width)) return new ScrollBarPath(areas, false);
        }
        return new ScrollBarPath(areas, true);
    }
}

// 直近の葉だけを判定スレッドで保持する。同一点はそのまま、移動先は葉の実行時 ID が一致したときに再利用する。
public sealed class ScrollBarCache
{
    private const int LifetimeMilliseconds = 2000;
    private long window;
    private PixelPoint lastPoint;
    private int[] leafId = [];
    private ScrollBarPath? path;
    private long storedAtMs;

    public void Remember(long window, int[] leafId, PixelPoint point, ScrollBarPath path, long nowMs)
    {
        this.window = window;
        this.leafId = (int[])leafId.Clone();
        this.path = path;
        lastPoint = point;
        storedAtMs = nowMs;
    }

    public bool TryAtPoint(long window, PixelPoint point, int width, long nowMs, out bool hit)
    {
        hit = false;
        return point == lastPoint && IsFresh(window, nowMs) && path!.TryClassify(point, width, out hit);
    }

    public bool TryForElement(long window, int[] leafId, PixelPoint point, int width, long nowMs, out bool hit)
    {
        hit = false;
        if (!IsFresh(window, nowMs) || leafId.Length == 0 || !leafId.AsSpan().SequenceEqual(this.leafId) ||
            !path!.TryClassify(point, width, out hit)) return false;
        lastPoint = point;
        return true;
    }

    private bool IsFresh(long candidate, long nowMs) => path != null && candidate != 0 && candidate == window &&
        nowMs >= storedAtMs && (ulong)(nowMs - storedAtMs) < LifetimeMilliseconds;
}
