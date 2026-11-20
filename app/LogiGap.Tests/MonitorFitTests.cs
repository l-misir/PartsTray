using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class MonitorFitTests
{
    [Fact]
    public void LargestFitSearchesDownwardAndStopsAtFirstFit()
    {
        var measured = new List<float>();
        float result = MonitorMath.LargestFit(px =>
        {
            measured.Add(px);
            return px > 20.5f ? (21, 15) : (20, 15);
        }, 20, 20f * 2 / 3, 21.5f, .25f);
        Assert.Equal(20.5f, result);
        Assert.Equal(new[] { 21.5f, 21.25f, 21f, 20.75f, 20.5f }, measured);
    }

    [Fact]
    public void LargestFitKeepsMaximumWhenItFits() =>
        Assert.Equal(30f, MonitorMath.LargestFit(_ => (20, 20), 20, 10, 30, .25f));

    [Fact]
    public void LargestFitReturnsMinimumWhenNothingFits() =>
        Assert.Equal(13.333333f, MonitorMath.LargestFit(_ => (21, 21), 20, 20f * 2 / 3, 30, .25f));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LargestFitRejectsWidthOrHeightOverflow(bool widthOverflows)
    {
        float result = MonitorMath.LargestFit(px => px > 18
            ? widthOverflows ? (21, 10) : (10, 21)
            : (20, 20), 20, 10, 20, .25f);
        Assert.Equal(18f, result);
    }

    [Fact]
    public void LargestFitAllowsFitOnlyAtMinimum() =>
        Assert.Equal(10f, MonitorMath.LargestFit(px => px <= 10 ? (20, 20) : (21, 21), 20, 10, 11, .25f));

    [Fact]
    public void LargestFitNeverMeasuresBelowMinimum()
    {
        float result = MonitorMath.LargestFit(px =>
        {
            Assert.InRange(px, 10.1f, 11f);
            return (21, 21);
        }, 20, 10.1f, 11, .25f);
        Assert.Equal(10.1f, result);
    }
}
