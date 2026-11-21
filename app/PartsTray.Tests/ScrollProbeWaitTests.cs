// UIA の待機期限と、専用スレッドが遅れて得た経路を次の判定でだけ使う動作を確かめます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class ScrollProbeWaitTests
{
    [Fact]
    public void SlowProviderFinishingWithinTheNewLimitMatchesOnTheFirstProbe()
    {
        using var lane = new SingleFlightWorker("遅く応答するスクロール判定");
        var reply = lane.RunForCache(active =>
        {
            // 模擬プロバイダーだけを旧上限より長く待たせ、300ms の境界には余裕を残す。
            Thread.Sleep(75);
            return active();
        }, ScrollBarPolicy.TimeoutMilliseconds);

        Assert.Equal(WorkerWaitStatus.Completed, reply.Status);
        Assert.True(reply.Result);
        Assert.InRange(reply.Elapsed.TotalMilliseconds, 50, 300);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TimeoutLeavesTheCurrentProbeUnmatchedAndCachesTheLateAnswer(bool scrollable)
    {
        Assert.Equal(300, ScrollBarPolicy.TimeoutMilliseconds);
        using var lane = new SingleFlightWorker("模擬スクロール判定");
        using var release = new ManualResetEventSlim();
        var cache = new ScrollBarCache();
        var point = new PixelPoint(190, 50);
        var guard = TimeSpan.FromSeconds(5);
        const long window = 10;
        int providerCalls = 0;

        bool Query(Func<bool> active)
        {
            if (cache.TryAtPoint(window, point, 17, 1, out bool cached)) return cached;
            Interlocked.Increment(ref providerCalls);
            release.Wait();
            var node = new ScrollBarNode(scrollable, new(0, 0, 200, 100), true);
            var path = ScrollBarPolicy.Inspect(point, 17, [node], active);
            if (path == null || !path.TryClassify(point, 17, out bool hit)) return false;
            cache.Remember(window, [1], point, path, 1);
            return hit;
        }

        try
        {
            var first = lane.RunForCache(Query, ScrollBarPolicy.TimeoutMilliseconds);
            Assert.Equal(WorkerWaitStatus.Timeout, first.Status);
            Assert.False(first.Result);
            Assert.True(first.Elapsed > TimeSpan.FromMilliseconds(50));
            Assert.Equal(WorkerWaitStatus.Busy, lane.RunForCache(Query, 300).Status);
        }
        finally
        {
            release.Set();
        }

        WorkerWaitResult<bool> next = default;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            next = lane.RunForCache(Query, 300);
            return next.Status != WorkerWaitStatus.Busy;
        }, guard));
        Assert.Equal(WorkerWaitStatus.Completed, next.Status);
        Assert.Equal(scrollable, next.Result);
        Assert.Equal(1, providerCalls);
        Assert.False(cache.TryAtPoint(window + 1, point, 17, 1, out _));
        Assert.False(cache.TryAtPoint(window, new(199, 50), 17, 1, out _));
    }

    [Fact]
    public void ProviderErrorIsDistinctFromCompletedNegativeAnswer()
    {
        using var failedLane = new SingleFlightWorker("異常応答");
        using var healthyLane = new SingleFlightWorker("正常応答");
        var error = failedLane.RunForCache<bool>(_ => throw new InvalidOperationException(), 300);
        var miss = healthyLane.RunForCache(_ => false, 300);
        Assert.Equal(WorkerWaitStatus.Faulted, error.Status);
        Assert.Equal(WorkerWaitStatus.Completed, miss.Status);
        Assert.False(miss.Result);
    }
}
