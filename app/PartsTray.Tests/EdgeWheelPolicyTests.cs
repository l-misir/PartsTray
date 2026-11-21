// モニターの物理座標と表示倍率から求める判定点、届け直しを許可する窓の条件を確かめます。
using System.Drawing;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class EdgeWheelPolicyTests
{
    // メイン画面の左上に置いた、右端が -1、下端が -201 のモニター。
    private static readonly Rectangle Monitor = new(-1920, -1280, 1920, 1080);

    [Theory]
    [InlineData(-1920, -500, 96, -1912, -500)]
    [InlineData(-1918, -500, 144, -1908, -500)]
    [InlineData(-1, -500, 96, -9, -500)]
    [InlineData(-3, -500, 120, -11, -500)]
    [InlineData(-800, -1278, 192, -800, -1264)]
    [InlineData(-800, -201, 144, -800, -213)]
    [InlineData(-2, -202, 96, -9, -209)]
    [InlineData(-1919, -1280, 120, -1910, -1270)]
    public void MovesOnlyTheAxesAtAnEdge(int x, int y, int dpi, int expectedX, int expectedY)
    {
        Assert.Equal(new PixelPoint(expectedX, expectedY),
            EdgeWheelPolicy.InwardPoint(new(x, y), Monitor, (uint)dpi));
    }

    [Theory]
    [InlineData(-1917, -500)]
    [InlineData(-4, -500)]
    [InlineData(-800, -1277)]
    [InlineData(-800, -204)]
    [InlineData(-700, -500)]
    [InlineData(0, -500)]
    [InlineData(-1921, -500)]
    [InlineData(-700, -200)]
    [InlineData(-700, -1281)]
    public void IgnoresInteriorAndNeighboringMonitorsEvenAtHighDpi(int x, int y)
    {
        Assert.Null(EdgeWheelPolicy.InwardPoint(new(x, y), Monitor, 192));
    }

    [Theory]
    [InlineData(-800, -1283, -800, -1280)]
    [InlineData(-1924, -500, -1920, -500)]
    [InlineData(5, -190, -1, -201)]
    [InlineData(-900, -700, -900, -700)]
    public void ClampsToTheNearestPixelOfTheSelectedMonitor(int x, int y, int expectedX, int expectedY)
    {
        Assert.Equal(new PixelPoint(expectedX, expectedY), EdgeWheelPolicy.ClampToMonitor(new(x, y), Monitor));
    }

    [Fact]
    public void ClampedTopEdgeUsesAnInwardPointAndKeepsTheHookPositionForTheCache()
    {
        var input = new PixelPoint(-500, -1284);
        var cursor = EdgeWheelPolicy.ClampToMonitor(input, Monitor);
        var points = new ScrollBarProbe(cursor, EdgeWheelPolicy.InwardPoint(cursor, Monitor, 96), input);
        Assert.Equal(new PixelPoint(-500, -1272), points.Inside);
        var cache = points.ToPointerCache(new FakeClock(), cursor);
        Assert.Equal(input, cache.Position);
        Assert.True(cache.VerticalScrollBar);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(-3, true)]
    [InlineData(-4, false)]
    public void DirectChromiumHitNeedsAnInsetAtTheTwoPixelEdge(int x, bool atEdge)
    {
        var cursor = new PixelPoint(x, -500);
        var points = new ScrollBarProbe(cursor, EdgeWheelPolicy.InwardPoint(cursor, Monitor, 96));
        var target = points.ChromiumTarget(cursor, 11, 11, 22);
        Assert.Equal(atEdge ? new EdgeWheelTarget(11, new(-9, -500)) : default, target);
    }

    [Fact]
    public void InwardChromiumHitPostsToTheInwardRendererAndRejectsMissingOrChangedMatches()
    {
        var cursor = new PixelPoint(-1, -500);
        var inside = new PixelPoint(-9, -500);
        var points = new ScrollBarProbe(cursor, inside);
        Assert.Equal(new EdgeWheelTarget(22, inside), points.ChromiumTarget(inside, 22, 11, 22));
        Assert.Equal(default, points.ChromiumTarget(inside, 11, 11, 22));
        Assert.Equal(default, points.ChromiumTarget(cursor, 22, 11, 22));
        Assert.Equal(default, points.ChromiumTarget(inside, 0, 11, 22));
        Assert.Equal(default, points.ChromiumTarget(null, 22, 11, 22));
        Assert.Equal(default, new ScrollBarProbe(cursor, null).ChromiumTarget(cursor, 11, 11, 22));
    }

    [Fact]
    public void ChromiumWheelPostsOneScaledDeltaWithSignedInwardCoordinates()
    {
        var target = new EdgeWheelTarget(22, new(-9, -720));
        var input = new PointerEvent(MouseEventKind.Wheel, new(-1, -720), Delta: -2000);
        int calls = 0;
        bool Post(nint window, uint message, nuint wParam, nint lParam)
        {
            calls++;
            Assert.Equal((nint)22, window);
            Assert.Equal(0x020Au, message);
            Assert.Equal(0x0Cu, (uint)wParam & 0xffff);
            Assert.Equal(-2000, unchecked((short)(wParam >> 16)));
            Assert.Equal(-9, unchecked((short)lParam));
            Assert.Equal(-720, unchecked((short)(lParam >> 16)));
            return true;
        }
        Assert.True(target.PostWheel(input, 0x0C, Post));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ChromiumWheelReportsAFailedPostWithoutRetrying()
    {
        var target = new EdgeWheelTarget(22, new(-9, -720));
        int calls = 0;
        Assert.False(target.PostWheel(new(MouseEventKind.Wheel, default, Delta: 6000), 0,
            (_, _, _, _) => { calls++; return false; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void UnrepresentableScreenCoordinatesKeepTheNormalSendInputRoute()
    {
        var points = new ScrollBarProbe(new(40000, 500), new(39992, 500));
        Assert.Equal(default, points.ChromiumTarget(points.Cursor, 22, 22, 22));
    }

    [Fact]
    public void AssistAcceptsMaximizedWorkAreaAndFullScreen()
    {
        var workArea = Monitor with { Height = Monitor.Height - 40 };
        Assert.True(EdgeWheelPolicy.CanProbe(true, workArea, Monitor));
        Assert.False(EdgeWheelPolicy.CanProbe(false, workArea, Monitor));
        Assert.True(EdgeWheelPolicy.CanProbe(false, Monitor, Monitor));
        Assert.False(EdgeWheelPolicy.CanProbe(false, new(0, 0, Monitor.Width, Monitor.Height), Monitor));
    }

    private static bool CanRedirect(bool maximized = true, long source = 10, long target = 11,
        bool sameRoot = true, bool chromium = false, bool ownProcess = false,
        bool elevatedTarget = false, bool administrator = false) =>
        EdgeWheelPolicy.CanRedirect(maximized, source, target, sameRoot, chromium, ownProcess, elevatedTarget, administrator);

    [Fact]
    public void RedirectRequiresDistinctWindowsUnderOneMaximizedRoot()
    {
        Assert.True(CanRedirect());
        Assert.False(CanRedirect(maximized: false));
        Assert.False(CanRedirect(sameRoot: false));
        Assert.False(CanRedirect(source: 11));
        Assert.False(CanRedirect(source: 0));
        Assert.False(CanRedirect(target: 0));
    }

    [Fact]
    public void RedirectExcludesChromiumOwnProcessAndHigherPrivileges()
    {
        Assert.False(CanRedirect(chromium: true));
        Assert.False(CanRedirect(ownProcess: true));
        Assert.False(CanRedirect(elevatedTarget: true));
        Assert.True(CanRedirect(elevatedTarget: true, administrator: true));
        Assert.False(CanRedirect(chromium: true, administrator: true));
        Assert.False(CanRedirect(ownProcess: true, administrator: true));
    }

    [Fact]
    public void ChromiumEdgeCanPostToSameRendererButStillChecksWindowAndProcess()
    {
        static bool Allowed(bool eligible = true, bool sameRoot = true, bool chromium = true,
            bool own = false, bool elevated = false, bool administrator = false) =>
            EdgeWheelPolicy.CanRedirect(eligible, 11, 11, sameRoot, chromium, own, elevated, administrator, chromiumEdge: true);

        Assert.True(Allowed());
        Assert.False(Allowed(eligible: false));
        Assert.False(Allowed(sameRoot: false));
        Assert.False(Allowed(chromium: false));
        Assert.False(Allowed(own: true));
        Assert.False(Allowed(elevated: true));
        Assert.True(Allowed(elevated: true, administrator: true));
    }
}
