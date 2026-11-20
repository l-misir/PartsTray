using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WheelSmootherTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
    private static WheelSmoother Create(int smoothMs = 40, int intervalMs = 8)
    {
        var smoother = new WheelSmoother();
        smoother.Configure(smoothMs, intervalMs);
        return smoother;
    }

    [Theory]
    [InlineData(60, 12)]
    [InlineData(-60, -12)]
    public void SendsFiveSlicesAtDueTimes(int amount, int expected)
    {
        var s = Create(); s.Add(amount, Ms(0));
        Assert.Equal(Ms(0), s.NextDue);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(expected, s.Take(Ms(i * 8)));
            Assert.Equal(0, s.Take(Ms(i * 8 + 4)));
        }
        Assert.Null(s.NextDue);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(-3, -1)]
    public void SmallAmountsFinishEarly(int amount, int expected)
    {
        var s = Create(); s.Add(amount, Ms(0));
        Assert.Equal(expected, s.Take(Ms(0)));
        Assert.Equal(expected, s.Take(Ms(8)));
        Assert.Equal(expected, s.Take(Ms(16)));
        Assert.Null(s.NextDue);
        Assert.Equal(0, s.Take(Ms(24)));
    }

    [Fact]
    public void SameDirectionRedistributesRemainderWithoutChangingDueTime()
    {
        var s = Create(); s.Add(60, Ms(0));
        Assert.Equal(12, s.Take(Ms(0))); Assert.Equal(12, s.Take(Ms(8)));
        s.Add(60, Ms(10));
        Assert.Equal(Ms(16), s.NextDue);
        Assert.Equal(0, s.Take(Ms(10)));
        Assert.Equal(new[] { 20, 19, 19, 19, 19 }, Enumerable.Range(0, 5).Select(i => s.Take(Ms(16 + i * 8))));
        Assert.Null(s.NextDue);
    }

    [Fact]
    public void ReversalDiscardsOldDirectionAndStartsImmediately()
    {
        var s = Create(); s.Add(60, Ms(0)); Assert.Equal(12, s.Take(Ms(0)));
        s.Add(-60, Ms(3));
        Assert.Equal(Ms(3), s.NextDue);
        Assert.Equal(new[] { -12, -12, -12, -12, -12 }, Enumerable.Range(0, 5).Select(i => s.Take(Ms(3 + i * 8))));
        Assert.Null(s.NextDue);
    }

    [Fact]
    public void LateTakeDoesNotBurst()
    {
        var s = Create(); s.Add(60, Ms(0)); Assert.Equal(12, s.Take(Ms(0)));
        Assert.Equal(12, s.Take(Ms(50)));
        Assert.Equal(Ms(58), s.NextDue);
        Assert.Equal(0, s.Take(Ms(50)));
    }

    [Fact]
    public void ResetAndConfigureDiscardRemainder()
    {
        var s = Create(); s.Add(60, Ms(0)); s.Reset();
        Assert.Null(s.NextDue); Assert.Equal(0, s.Take(Ms(0)));
        s.Add(60, Ms(0)); s.Configure(40, 8);
        Assert.Null(s.NextDue); Assert.Equal(0, s.Take(Ms(100)));
    }

    [Fact]
    public void ZeroDoesNotExtendOrReschedule()
    {
        var s = Create(); s.Add(0, Ms(0)); Assert.Null(s.NextDue);
        s.Add(60, Ms(0)); s.Take(Ms(0)); s.Add(0, Ms(3));
        Assert.Equal(Ms(8), s.NextDue);
        Assert.Equal(48, Enumerable.Range(1, 4).Sum(i => s.Take(Ms(i * 8))));
        Assert.Null(s.NextDue);
    }

    [Theory]
    [InlineData(0, 8, 1)]
    [InlineData(41, 8, 6)]
    [InlineData(200, 4, 50)]
    public void SliceCountRoundsUp(int smoothMs, int intervalMs, int count)
    {
        var s = Create(smoothMs, intervalMs); s.Add(300, Ms(0));
        Assert.Equal(300, Enumerable.Range(0, count).Sum(i => s.Take(Ms(i * intervalMs))));
        Assert.Null(s.NextDue);
    }
}
