// 実際の入力を送らず、フックと同じ入口で高速スクロール・届け直し・素通しの優先順を確かめます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class EdgeWheelGestureTests
{
    private static readonly PixelPoint Edge = new(-1, -720);
    private static readonly EdgeWheelTarget Target = new(18, new(-9, -720));

    private sealed class WheelEffects : IGestureEffects
    {
        public readonly List<GestureEffect> Posted = [];
        public readonly List<(PointerEvent Input, EdgeWheelTarget Target)> Redirected = [];
        public bool Accepted = true;

        public void Post(GestureEffect effect) => Posted.Add(effect);

        public bool TryRedirectWheel(PointerEvent input, EdgeWheelTarget target)
        {
            Redirected.Add((input, target));
            return Accepted;
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void VerticalFastScrollWinsOverRedirect(bool directHit, bool edgeAssist, bool edgeHit)
    {
        var effects = new WheelEffects();
        var wheel = new WheelSettings { FastScrollOnScrollBar = true, FastScrollMultiplier = 3,
            EdgeAssist = edgeAssist, EdgeRedirect = true };
        var engine = new GestureEngine(new FakeClock(), effects, new() { Wheel = wheel });
        engine.SetCache(new(Edge, TimeSpan.Zero, directHit, EdgeScrollBar: edgeHit, Redirect: Target));

        Assert.True(engine.Handle(new(MouseEventKind.Wheel, Edge, Delta: -40)));
        var fast = Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        Assert.Equal(-120, fast.Input.Delta);
        Assert.Empty(effects.Redirected);
    }

    [Theory]
    [InlineData(true, false, true, false, true)]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, false, false, true, false)]
    public void FastWheelUsesTheCachedChromiumTargetWhenEdgeAssistIsOn(
        bool direct, bool inward, bool assist, bool redirect, bool postToRenderer)
    {
        var effects = new WheelEffects();
        var wheel = new WheelSettings { FastScrollOnScrollBar = true, FastScrollMultiplier = 50,
            EdgeAssist = assist, EdgeRedirect = redirect };
        var engine = new GestureEngine(new FakeClock(), effects, new() { Wheel = wheel });
        engine.SetCache(new(Edge, TimeSpan.Zero, direct, EdgeScrollBar: inward, ChromiumEdge: Target));
        var input = new PointerEvent(MouseEventKind.Wheel, Edge, Delta: -40, Modifiers: PointerModifiers.Shift);

        Assert.True(engine.Handle(input));
        var fast = Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        Assert.Equal(postToRenderer ? Target : default, fast.Target);
        Assert.Equal(input with { Delta = -2000 }, fast.Input);
        Assert.Empty(effects.Redirected);
    }

    [Theory]
    [InlineData(false, 1, -15, -15)]
    [InlineData(true, 1, 15, 15)]
    [InlineData(false, 100, -480, -48000)]
    [InlineData(false, 100, 480, 48000)]
    [InlineData(true, 100, -480, short.MinValue)]
    [InlineData(true, 100, 480, short.MaxValue)]
    public void SingleFastWheelKeepsDirectionAndUsesTheRangeOfEachTransport(
        bool chromium, int multiplier, int delta, int sentDelta)
    {
        var effects = new WheelEffects();
        var engine = new GestureEngine(new FakeClock(), effects, new() { Wheel = new()
            { FastScrollOnScrollBar = true, FastScrollMultiplier = multiplier, EdgeAssist = true } });
        var target = chromium ? Target : default;
        engine.SetCache(new(Edge, TimeSpan.Zero, true, ChromiumEdge: target));

        Assert.True(engine.Handle(new(MouseEventKind.Wheel, Edge, Delta: delta)));
        var fast = Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        Assert.Equal(sentDelta, fast.Input.Delta);
        Assert.Equal(target, fast.Target);
    }

    [Fact]
    public void ChromiumExceptionRequiresVerticalFastScrollAndFreshCacheAtInputPoint()
    {
        var effects = new WheelEffects();
        var clock = new FakeClock();
        var settings = new AppSettings { Wheel = new() { FastScrollOnScrollBar = true, EdgeAssist = true, EdgeRedirect = true } };
        var engine = new GestureEngine(clock, effects, settings);
        var cache = new PointerCache(Edge, clock.Now, true, ChromiumEdge: Target);
        engine.SetCache(cache);
        var input = new PointerEvent(MouseEventKind.Wheel, Edge, Delta: 120);

        Assert.False(engine.Handle(input with { Position = Target.Position }));
        Assert.False(engine.Handle(input with { Kind = MouseEventKind.HorizontalWheel }));
        clock.Now = TimeSpan.FromMilliseconds(101);
        Assert.False(engine.Handle(input));
        engine.UpdateSettings(settings with { Wheel = settings.Wheel with { FastScrollOnScrollBar = false } });
        engine.SetCache(cache with { At = clock.Now });
        Assert.False(engine.Handle(input));
        Assert.DoesNotContain(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        Assert.Empty(effects.Redirected);
    }

    [Theory]
    [InlineData(MouseEventKind.Wheel, false, true, -40)]
    [InlineData(MouseEventKind.Wheel, true, false, 240)]
    [InlineData(MouseEventKind.HorizontalWheel, true, true, -30)]
    [InlineData(MouseEventKind.HorizontalWheel, false, false, 15)]
    public void RedirectPreservesWheelAxisDeltaAndModifiers(MouseEventKind axis, bool fast, bool assist, int delta)
    {
        var effects = new WheelEffects();
        var wheel = new WheelSettings { FastScrollOnScrollBar = fast, EdgeAssist = assist, EdgeRedirect = true };
        var engine = new GestureEngine(new FakeClock(), effects, new() { Wheel = wheel });
        engine.SetCache(new(Edge, TimeSpan.Zero, false, EdgeScrollBar: true, Redirect: Target));
        var input = new PointerEvent(axis, Edge, Delta: delta, Modifiers: PointerModifiers.Control | PointerModifiers.Shift);

        Assert.True(engine.Handle(input));
        Assert.Equal((input, Target), Assert.Single(effects.Redirected));
        Assert.DoesNotContain(effects.Posted, e => e.Kind is GestureEffectKind.Replay or GestureEffectKind.FastScroll);
    }

    [Fact]
    public void RedirectNeedsFreshCacheForExactPositionAndSuccessfulDelivery()
    {
        var clock = new FakeClock();
        var effects = new WheelEffects { Accepted = false };
        var engine = new GestureEngine(clock, effects, new() { Wheel = new() { EdgeRedirect = true } });
        var input = new PointerEvent(MouseEventKind.Wheel, Edge, Delta: 120);
        Assert.False(engine.Handle(input));
        engine.SetCache(new(Edge, clock.Now, false));
        Assert.False(engine.Handle(input));
        engine.SetCache(new(Edge, clock.Now, false, Redirect: Target));
        Assert.False(engine.Handle(input with { Position = new(Edge.X, Edge.Y + 1) }));
        clock.Now = TimeSpan.FromMilliseconds(101);
        Assert.False(engine.Handle(input));
        Assert.Empty(effects.Redirected);

        engine.SetCache(new(Edge, clock.Now, false, Redirect: Target));
        Assert.False(engine.Handle(input));
        Assert.Single(effects.Redirected);
        Assert.DoesNotContain(effects.Posted, e => e.Kind is GestureEffectKind.Replay or GestureEffectKind.FastScroll);
    }

    [Fact]
    public void SlowInwardProbeRemainsFreshAtTheActualWheelPosition()
    {
        var clock = new FakeClock();
        var effects = new WheelEffects();
        var engine = new GestureEngine(clock, effects, new() { Wheel = new() { FastScrollOnScrollBar = true, FastScrollMultiplier = 3 } });
        engine.Handle(new(MouseEventKind.Move, Edge));
        var request = Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.Probe);
        var points = new ScrollBarProbe(request.Input.Position, Target.Position);
        clock.Now += TimeSpan.FromMilliseconds(240);
        engine.SetCache(points.ToPointerCache(clock, Target.Position));

        var wheel = new PointerEvent(MouseEventKind.Wheel, Edge, Delta: 120);
        Assert.False(engine.Handle(wheel with { Position = Target.Position }));
        Assert.True(engine.Handle(wheel));
        var fast = Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        Assert.Equal(wheel with { Delta = 360 }, fast.Input);
        clock.Now += TimeSpan.FromMilliseconds(101);
        Assert.False(engine.Handle(wheel));
    }

    [Fact]
    public void DisabledRedirectOrOwnInputPassesThroughEvenWithTargetCached()
    {
        var effects = new WheelEffects();
        var engine = new GestureEngine(new FakeClock(), effects, new());
        var input = new PointerEvent(MouseEventKind.HorizontalWheel, Edge, Delta: 120);
        var cache = new PointerCache(Edge, TimeSpan.Zero, false, Redirect: Target);
        engine.SetCache(cache);
        Assert.False(engine.Handle(input));
        engine.UpdateSettings(new() { Wheel = new() { EdgeRedirect = true } });
        engine.SetCache(cache);
        Assert.False(engine.Handle(input with { ExtraInfo = KeyStroke.ExtraInfo }));
        Assert.Empty(effects.Redirected);
    }

    [Fact]
    public void ReportsOnlyTheFirstSuccessfulPostAcrossBothAxes()
    {
        var effects = new WheelEffects { Accepted = false };
        var engine = new GestureEngine(new FakeClock(), effects, new() { Wheel = new() { EdgeRedirect = true } });
        engine.SetCache(new(Edge, TimeSpan.Zero, false, Redirect: Target));
        var input = new PointerEvent(MouseEventKind.Wheel, Edge, Delta: 120);

        Assert.False(engine.Handle(input));
        Assert.DoesNotContain(effects.Posted, e => e.Kind == GestureEffectKind.EdgeRedirectReport);
        effects.Accepted = true;
        Assert.True(engine.Handle(input));
        Assert.True(engine.Handle(input with { Kind = MouseEventKind.HorizontalWheel }));
        Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.EdgeRedirectReport);
    }

    [Fact]
    public void EdgeTargetDoesNotInterceptClicksOrOverrideRightWheelSwitch()
    {
        var effects = new WheelEffects();
        var settings = new AppSettings { Gesture = new() { Enabled = true, RightWheelSwitch = true },
            Wheel = new() { FastScrollOnScrollBar = true, EdgeRedirect = true } };
        var engine = new GestureEngine(new FakeClock(), effects, settings);
        engine.SetCache(new(Edge, TimeSpan.Zero, false, EdgeScrollBar: true, Redirect: Target));

        Assert.False(engine.Handle(new(MouseEventKind.Down, Edge, PointerButton.Left)));
        Assert.False(engine.Handle(new(MouseEventKind.Up, Edge, PointerButton.Left)));
        Assert.True(engine.Handle(new(MouseEventKind.Down, Edge, PointerButton.Right)));
        Assert.True(engine.Handle(new(MouseEventKind.Wheel, Edge, Delta: 120)));
        Assert.Contains(effects.Posted, e => e.Kind == GestureEffectKind.SwitchMaximized);
        Assert.DoesNotContain(effects.Posted, e => e.Kind is GestureEffectKind.Replay or GestureEffectKind.FastScroll);
        Assert.Empty(effects.Redirected);
    }
}
