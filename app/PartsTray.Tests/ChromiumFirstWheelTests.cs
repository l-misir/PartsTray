// Chromium の最初の回転が、マウス移動や同一点の判定完了を待たずに届くことを確かめます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class ChromiumFirstWheelTests
{
    private static readonly PixelPoint Cursor = new(-1, 500), Inside = new(-9, 500);
    private static readonly ScrollBarBounds Client = new(-1000, 50, 0, 1000);
    private static readonly AppSettings Settings = new()
    {
        Wheel = new() { FastScrollOnScrollBar = true, FastScrollMultiplier = 50, EdgeAssist = true, EdgeRedirect = false }
    };
    private sealed class Effects : IGestureEffects
    {
        public readonly List<(PointerEvent Input, EdgeWheelTarget Target)> Deliveries = [];
        public readonly List<GestureEffect> Posted = [];
        public string Process = "Code.exe";
        public bool Accept = true;
        public int Lookups;
        public void Post(GestureEffect effect) => Posted.Add(effect);
        public EdgeWheelTarget FirstChromiumEdge(PixelPoint point, WheelSettings wheel)
        {
            Lookups++;
            return wheel.AllowsFastScroll(Process) && point == Cursor
                ? new ScrollBarProbe(point, Inside).FirstChromiumTarget(10, null, 12, Client, 17) : default;
        }
        public bool TryRedirectWheel(PointerEvent input, EdgeWheelTarget target)
        {
            Deliveries.Add((input, target));
            return Accept;
        }
    }

    [Fact]
    public void FirstWheelIsDeliveredInwardBeforeProbeReplyThenCachedHitUsesMultiplier()
    {
        var effects = new Effects();
        var clock = new FakeClock();
        var engine = new GestureEngine(clock, effects, Settings);
        var input = new PointerEvent(MouseEventKind.Wheel, Cursor, Delta: -40, Modifiers: PointerModifiers.Shift);
        var target = new EdgeWheelTarget(12, Inside);

        Assert.True(engine.Handle(input));
        Assert.Equal((input, target), Assert.Single(effects.Deliveries));
        Assert.DoesNotContain(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        engine.SetCache(new(Cursor, clock.Now, false, EdgeScrollBar: true, ChromiumEdge: target));
        Assert.True(engine.Handle(input));
        var fast = Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        Assert.Equal(target, fast.Target);
        Assert.Equal(-2000, fast.Input.Delta);
        Assert.Single(effects.Deliveries);

        clock.Now += TimeSpan.FromSeconds(10);
        Assert.True(engine.Handle(input));
        Assert.Equal(2, effects.Deliveries.Count);
        Assert.Equal((input, target), effects.Deliveries[1]);
    }

    [Theory]
    [InlineData(false, true, MouseEventKind.Wheel)]
    [InlineData(true, false, MouseEventKind.Wheel)]
    [InlineData(true, true, MouseEventKind.HorizontalWheel)]
    public void DisabledAssistOrHorizontalInputBypassesFirstWheelLookup(bool fast, bool assist, MouseEventKind kind)
    {
        var effects = new Effects();
        var settings = Settings with { Wheel = Settings.Wheel with { FastScrollOnScrollBar = fast, EdgeAssist = assist } };
        var engine = new GestureEngine(new FakeClock(), effects, settings);
        Assert.False(engine.Handle(new(kind, Cursor, Delta: 120)));
        Assert.Empty(effects.Deliveries);
        Assert.Equal(0, effects.Lookups);
    }

    [Fact]
    public void FirstWheelPassesThroughWhenExcludedOrPostingFails()
    {
        var effects = new Effects { Process = "acrobat.EXE" };
        var engine = new GestureEngine(new FakeClock(), effects, Settings);
        var input = new PointerEvent(MouseEventKind.Wheel, Cursor, Delta: 120);
        Assert.False(engine.Handle(input));
        Assert.Empty(effects.Deliveries);
        effects.Process = "Code.exe";
        effects.Accept = false;
        Assert.False(engine.Handle(input));
        Assert.Single(effects.Deliveries);
        Assert.DoesNotContain(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll || e.Valid);
    }

    [Fact]
    public void OnlyAMatchingPreparedRendererAtTheCursorOrInsideCanReceiveFirstWheel()
    {
        var points = new ScrollBarProbe(Cursor, Inside);
        Assert.Equal(new EdgeWheelTarget(10, Inside), points.FirstChromiumTarget(10, Client, 12, Client, 17));
        Assert.Equal(new EdgeWheelTarget(12, Inside), points.FirstChromiumTarget(10, null, 12, Client, 17));
        Assert.Equal(default, points.FirstChromiumTarget(10, null, 12, null, 17));
        var outsideBand = Client with { Right = 100 };
        Assert.Equal(default, points.FirstChromiumTarget(10, outsideBand, 12, outsideBand, 17));
    }
}
