// UI オートメーションの縦スクロール判定と待機上限を、偽の要素と問い合わせで確かめます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class ScrollBarPolicyTests
{
    private static readonly ScrollBarBounds Pane = new(20, 40, 300, 200);
    private static readonly PixelPoint OnBar = new(295, 120);

    [Theory]
    [InlineData(17, 281)] // 96 DPI で幅が 17 px の場合
    [InlineData(26, 272)] // 144 DPI で幅が 26 px の場合
    [InlineData(34, 264)] // 192 DPI で幅が 34 px の場合
    public void AddsTwoPhysicalPixelsToTheSuppliedMetric(int metric, int expectedLeft)
    {
        Assert.False(Pane.InRightBand(new(expectedLeft - 1, 100), metric));
        Assert.True(Pane.InRightBand(new(expectedLeft, 100), metric));
        Assert.True(Pane.InRightBand(new(299, 40), metric));
        Assert.False(Pane.InRightBand(new(300, 40), metric));
        Assert.False(Pane.InRightBand(new(299, 39), metric));
        Assert.False(Pane.InRightBand(new(299, 200), metric));
    }

    [Fact]
    public void BandStaysInsideNarrowFractionalBoundsOnANegativeCoordinateMonitor()
    {
        var bounds = new ScrollBarBounds(-51.5, -20.5, -45.25, -10.5);
        Assert.True(bounds.InRightBand(new(-51, -20), 17));
        Assert.True(bounds.InRightBand(new(-46, -11), 17));
        Assert.False(bounds.InRightBand(new(-52, -20), 17));
        Assert.False(bounds.InRightBand(new(-45, -11), 17));
    }

    [Fact]
    public void EmptyOrNonfiniteBoundsAndMissingMetricCannotMatch()
    {
        Assert.False(new ScrollBarBounds(295, 40, 295, 200).InRightBand(OnBar, 17));
        Assert.False(new ScrollBarBounds(20, 120, 300, 120).InRightBand(OnBar, 17));
        Assert.False(new ScrollBarBounds(double.NegativeInfinity, 40, 300, 200).InRightBand(OnBar, 17));
        Assert.False(Pane.InRightBand(OnBar, 0));
    }

    private static ScrollBarPath Inspect(PixelPoint point, params ScrollBarNode[] nodes) =>
        Assert.IsType<ScrollBarPath>(ScrollBarPolicy.Inspect(point, 17, nodes, () => true));

    private static void AssertHit(ScrollBarPath path, PixelPoint point, bool expected)
    {
        Assert.True(path.TryClassify(point, 17, out bool hit));
        Assert.Equal(expected, hit);
    }

    [Fact]
    public void NonScrollableLeafAndWindowRootCannotMatchEvenInsideTheirBands()
    {
        var path = Inspect(OnBar, new(false, Pane, false), new(false, Pane, true));
        AssertHit(path, OnBar, false);
    }

    [Fact]
    public void WindowRootMayMatchButDesktopParentIsNeverRequested()
    {
        IEnumerable<ScrollBarNode> Ancestors(bool scrollableRoot)
        {
            yield return new(false, Pane, false);
            yield return new(scrollableRoot, Pane, true);
            throw new InvalidOperationException("ウィンドウのルートを越えて親を取得しました");
        }
        foreach (bool scrollable in new[] { true, false })
        {
            var path = ScrollBarPolicy.Inspect(OnBar, 17, Ancestors(scrollable), () => true);
            AssertHit(Assert.IsType<ScrollBarPath>(path), OnBar, scrollable);
        }
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void ChecksDeepScrollContainersWithoutRequestingTheParentBeyondTheLimit(int scrollableDepth, bool expected)
    {
        var visited = new List<int>();
        IEnumerable<ScrollBarNode> Ancestors()
        {
            for (int depth = 0; depth <= 33; depth++)
            {
                visited.Add(depth);
                yield return new(depth == scrollableDepth, Pane, false);
            }
        }
        var path = ScrollBarPolicy.Inspect(OnBar, 17, Ancestors(), () => true);
        AssertHit(Assert.IsType<ScrollBarPath>(path), OnBar, expected);
        Assert.Equal(Enumerable.Range(0, Math.Min(scrollableDepth, 32) + 1), visited);
    }

    [Fact]
    public void ExplicitVerticalBarMatchesOnlyItsOwnRectangleWithoutAScrollPattern()
    {
        var bounds = new ScrollBarBounds(285, 50, 300, 180);
        ScrollBarNode[] nodes = [new(false, Pane, false), new(false, bounds, false, IsVerticalBar: true), new(false, Pane, true)];
        AssertHit(Inspect(OnBar, nodes), OnBar, true);
        foreach (var point in new[] { new PixelPoint(284, 120), new PixelPoint(300, 120), new PixelPoint(295, 180) })
            AssertHit(Inspect(point, nodes), point, false);
    }

    [Fact]
    public void NestedScrollablePaneCanExposeItsOwnBandOrItsParentsBand()
    {
        var inner = new ScrollBarBounds(180, 40, 300, 200);
        var outer = new ScrollBarBounds(100, 20, 305, 250);
        ScrollBarNode[] nodes = [new(false, inner, false), new(true, inner, false), new(true, outer, true)];
        foreach (var point in new[] { new PixelPoint(282, 120), new PixelPoint(303, 120) })
            AssertHit(Inspect(point, nodes), point, true);
        var insideContent = new PixelPoint(220, 120);
        AssertHit(Inspect(insideContent, nodes), insideContent, false);
    }

    [Fact]
    public void StoppedWorkerDoesNotStartAProviderCall()
    {
        IEnumerable<ScrollBarNode> Nodes()
        {
            yield return Read();
        }
        static ScrollBarNode Read() => throw new InvalidOperationException("終了後に要素を取得しました");
        Assert.Null(ScrollBarPolicy.Inspect(OnBar, 17, Nodes(), () => false));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ChecksIfWorkerWasStoppedDuringAProviderCall(int stopAtDepth, bool completes)
    {
        bool active = true;
        IEnumerable<ScrollBarNode> Nodes()
        {
            active = stopAtDepth != 0;
            yield return new(false, Pane, false);
            active = stopAtDepth != 1;
            yield return new(true, Pane, true);
        }
        var path = ScrollBarPolicy.Inspect(OnBar, 17, Nodes(), () => active);
        if (completes) AssertHit(Assert.IsType<ScrollBarPath>(path), OnBar, true);
        else Assert.Null(path);
    }

    [Fact]
    public void CacheReusesTheSampleForTwoSecondsWithoutExtendingItsLifetime()
    {
        var cache = new ScrollBarCache();
        cache.Remember(10, [1, 2], OnBar, Inspect(OnBar, new ScrollBarNode(true, Pane, true)), 500);
        Assert.True(cache.TryAtPoint(10, OnBar, 17, 2499, out bool hit));
        Assert.True(hit);
        Assert.False(cache.TryAtPoint(10, OnBar, 17, 2500, out _));
        Assert.False(cache.TryForElement(10, [1, 2], OnBar, 17, 2500, out _));
    }

    [Fact]
    public void CachedRectangleUsesTheMetricForTheCurrentProbe()
    {
        var cache = new ScrollBarCache();
        var point = new PixelPoint(278, 120);
        cache.Remember(10, [1], point, Inspect(point, new ScrollBarNode(true, Pane, true)), 100);
        Assert.True(cache.TryAtPoint(10, point, 17, 101, out bool narrowHit));
        Assert.False(narrowHit);
        Assert.True(cache.TryAtPoint(10, point, 26, 102, out bool wideHit));
        Assert.True(wideHit);
    }

    [Fact]
    public void AnotherPointRequiresMatchingWindowAndElementIdentity()
    {
        var cache = new ScrollBarCache();
        var nearby = new PixelPoint(291, 150);
        cache.Remember(10, [7, 8], OnBar, Inspect(OnBar, new ScrollBarNode(true, Pane, true)), 100);
        Assert.False(cache.TryAtPoint(11, OnBar, 17, 101, out _));
        Assert.False(cache.TryAtPoint(10, nearby, 17, 101, out _));
        Assert.False(cache.TryForElement(10, [7, 9], nearby, 17, 101, out _));
        Assert.False(cache.TryForElement(11, [7, 8], nearby, 17, 101, out _));
        Assert.True(cache.TryForElement(10, [7, 8], nearby, 17, 101, out bool hit));
        Assert.True(hit);
    }

    [Fact]
    public void MissingRuntimeIdAllowsReuseOnlyAtTheSamePointOfTheSameWindow()
    {
        var cache = new ScrollBarCache();
        cache.Remember(10, [], OnBar, Inspect(OnBar, new ScrollBarNode(true, Pane, true)), 100);
        Assert.True(cache.TryAtPoint(10, OnBar, 17, 101, out bool hit));
        Assert.True(hit);
        Assert.False(cache.TryAtPoint(11, OnBar, 17, 101, out _));
        Assert.False(cache.TryForElement(10, [], new(290, 122), 17, 101, out _));
    }

    [Fact]
    public void PartialPathCannotRuleOutAnUnvisitedParentsBand()
    {
        var nearby = new PixelPoint(304, 122);
        var outer = new ScrollBarBounds(0, 20, 306, 250);
        ScrollBarNode[] nodes = [new(true, Pane, false), new(true, outer, true)];
        var path = Inspect(OnBar, nodes);
        Assert.False(path.TryClassify(nearby, 17, out _));
        AssertHit(Inspect(nearby, nodes), nearby, true);
    }

    [Fact]
    public async Task UnresponsiveProviderTimesOutWithoutStartingASecondRequest()
    {
        using var lane = new SingleFlightWorker("偽の UI オートメーション");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard = TimeSpan.FromSeconds(5);
        int otherCalls = 0;
        bool Query(Func<bool> _)
        {
            entered.SetResult();
            release.Wait();
            finished.SetResult();
            return true;
        }

        var first = Task.Run(() => lane.RunWithTimeout(Query, 500, false));
        try
        {
            await entered.Task.WaitAsync(guard);
            Assert.False(await first.WaitAsync(guard));
            Assert.False(lane.RunWithTimeout(_ => { Interlocked.Increment(ref otherCalls); return true; }, 500, false));
            Assert.Equal(0, otherCalls);
        }
        finally
        {
            release.Set();
        }
        await finished.Task.WaitAsync(guard);
        Assert.True(SpinWait.SpinUntil(() => lane.RunWithTimeout(_ => true, 500, false), guard));
        Assert.Equal(0, otherCalls);
    }
}
