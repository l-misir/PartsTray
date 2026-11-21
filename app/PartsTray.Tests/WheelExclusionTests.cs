// 高速スクロールの除外と、カーソル下の窓や設定が変わった直後の素通しを確かめます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WheelExclusionTests
{
    private static readonly PixelPoint Edge = new(1919, 300);
    private static readonly EdgeWheelTarget Inside = new(8, new(1911, 300));
    private static readonly WheelWindow Window = new(7, 42, Edge);
    private static readonly AppSettings Settings = new() { Wheel = new() { FastScrollOnScrollBar = true, EdgeRedirect = true } };

    private sealed class Effects : IGestureEffects
    {
        public WheelWindow Current = Window;
        public List<GestureEffect> Posted { get; } = [];
        public int Redirects;
        public void Post(GestureEffect effect) => Posted.Add(effect);
        public bool IsCurrentWheelWindow(WheelWindow window) => window == Current;
        public bool TryRedirectWheel(PointerEvent input, EdgeWheelTarget target) { Redirects++; return true; }

        public void AssertNoDelivery()
        {
            Assert.Equal(0, Redirects);
            Assert.DoesNotContain(Posted, e => e.Kind is GestureEffectKind.FastScroll or GestureEffectKind.Replay or GestureEffectKind.EdgeRedirectReport);
        }
    }

    [Theory]
    [InlineData("ACROBAT.EXE", false)]
    [InlineData("acrord32.ExE", false)]
    [InlineData("Code.exe", true)]
    [InlineData("MyAcrobat.exe", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void DefaultExclusionsMatchWholeProcessNames(string? processName, bool allowed)
        => Assert.Equal(allowed, Settings.Wheel.AllowsFastScroll(processName));

    [Fact]
    public void EditingTheListReplacesDefaultsAndAnEmptyListNeedsNoProcessName()
    {
        var edited = Settings.Wheel with { FastScrollExcludedProcesses = ["Code.exe"] };
        Assert.False(edited.AllowsFastScroll("code.EXE"));
        Assert.True(edited.AllowsFastScroll("Acrobat.exe"));
        Assert.True((edited with { FastScrollExcludedProcesses = [] }).AllowsFastScroll(null));
    }

    [Theory]
    [InlineData(MouseEventKind.Wheel, true, false, false)]
    [InlineData(MouseEventKind.Wheel, false, true, false)]
    [InlineData(MouseEventKind.Wheel, false, true, true)]
    [InlineData(MouseEventKind.Wheel, false, false, false)]
    [InlineData(MouseEventKind.HorizontalWheel, false, false, false)]
    public void ExcludedWindowPassesThroughEvenWhenScrollBarsOrRedirectTargetsAreCached(
        MouseEventKind axis, bool direct, bool inward, bool renderer)
    {
        var effects = new Effects();
        var engine = new GestureEngine(new FakeClock(), effects, Settings);
        engine.SetCache(new(Edge, TimeSpan.Zero, direct, EdgeScrollBar: inward, Redirect: Inside,
            ChromiumEdge: renderer ? Inside : default, WheelWindow: Window, WheelExcluded: true));

        Assert.False(engine.Handle(new(axis, Edge, Delta: -40)));
        effects.AssertNoDelivery();
    }

    [Theory]
    [InlineData(MouseEventKind.Wheel, true)]
    [InlineData(MouseEventKind.Wheel, false)]
    [InlineData(MouseEventKind.HorizontalWheel, false)]
    public void NewWindowAtTheSamePointCannotUseThePreviousWindowsScrollTargets(MouseEventKind axis, bool fast)
    {
        var effects = new Effects { Current = new(12, 45, Edge) };
        var engine = new GestureEngine(new FakeClock(), effects, Settings);
        engine.SetCache(new(Edge, TimeSpan.Zero, fast, Redirect: Inside, ChromiumEdge: Inside, WheelWindow: Window));

        Assert.False(engine.Handle(new(axis, Edge, Delta: 120)));
        effects.AssertNoDelivery();
    }

    [Fact]
    public void EditingExclusionsDiscardsThePreviousAllowedResult()
    {
        var effects = new Effects();
        var engine = new GestureEngine(new FakeClock(), effects, Settings);
        engine.SetCache(new(Edge, TimeSpan.Zero, true, WheelWindow: Window));
        var wheel = new PointerEvent(MouseEventKind.Wheel, Edge, Delta: 120);
        Assert.True(engine.Handle(wheel));
        Assert.Contains(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);
        effects.Posted.Clear();

        engine.UpdateSettings(Settings with { Wheel = Settings.Wheel with { FastScrollExcludedProcesses = ["Code.exe"] } });
        Assert.False(engine.Handle(wheel));
        effects.AssertNoDelivery();
    }

    [Theory]
    [InlineData(false, 7L, 42u, "Code.exe", true, -200, false)]
    [InlineData(true, 7L, 42u, "Code.exe", true, -200, true)]
    [InlineData(false, 9L, 50u, "Acrobat.exe", true, -40, false)]
    [InlineData(true, 9L, 42u, "Code.exe", true, -40, false)]
    [InlineData(true, 7L, 99u, "Code.exe", true, -40, false)]
    [InlineData(false, 7L, 42u, "Acrobat.exe", true, -40, false)]
    [InlineData(true, 7L, 42u, "Code.exe", false, -40, false)]
    public void QueuedFastWheelChecksTheCurrentCursorWindowAndRendererBeforeDelivery(
        bool chromium, long root, uint pid, string process, bool targetCurrent, int expectedDelta, bool post)
    {
        var effects = new Effects();
        var engine = new GestureEngine(new FakeClock(), effects, Settings);
        var input = new PointerEvent(MouseEventKind.Wheel, Edge, Delta: -40, Modifiers: PointerModifiers.Control);
        engine.SetCache(new(Edge, TimeSpan.Zero, true, ChromiumEdge: chromium ? Inside : default, WheelWindow: Window));
        Assert.True(engine.Handle(input));
        var queued = Assert.Single(effects.Posted, e => e.Kind == GestureEffectKind.FastScroll);

        var delivery = queued.FastWheelDelivery(Settings.Wheel, root, pid, process, targetCurrent);
        Assert.Equal(input with { Delta = expectedDelta }, delivery.Input);
        Assert.Equal(post ? Inside : default, delivery.Target);
        var edited = Settings.Wheel with { FastScrollExcludedProcesses = ["CODE.EXE"] };
        Assert.Equal((input, default(EdgeWheelTarget)), queued.FastWheelDelivery(edited, Window.Root, Window.ProcessId, "Code.exe", true));
    }

}
