// 右ボタン＋ホイールの入力抑止・切り替え効果・解放時の記録を検証します。
// ジェスチャーとホイール切り替えの優先順位を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class RightWheelSwitchTests
{
    private sealed class Hits : IGestureHitTest
    {
        public HitResult HitTest(PointerEvent input, AppSettings settings) => new(42, true);
        public PointerCache Probe(PixelPoint point, TimeSpan at) => new(point, at, true);
    }
    private sealed class H : IGestureEffects
    {
        public readonly FakeClock Clock = new();
        public readonly List<GestureEffect> Effects = [];
        public readonly GestureEngine Engine;
        public H(bool enabled = true, bool gesture = true, int step = 120, bool reverse = false, int ms = 1500)
        {
            Engine = new(Clock, this, new()
            {
                Gesture = new() { Enabled = gesture, RightWheelSwitch = enabled },
                WindowKeys = new() { WheelSwitchStep = step, ReverseHorizontal = reverse, WheelSwitchMs = ms },
                Wheel = new() { FocusUnderCursor = true, FastScrollOnScrollBar = true }
            });
        }
        public void Post(GestureEffect e) => Effects.Add(e);
        public bool Event(MouseEventKind kind, int delta = 0, int x = 0, PointerButton button = PointerButton.Right)
            => Engine.Handle(new(kind, new(x, 0), button, delta));
        public long Down(bool resolve = true)
        {
            Assert.True(Event(MouseEventKind.Down));
            long token = Effects.Last(e => e.Kind == GestureEffectKind.HitTest).Token;
            if (resolve) Engine.Resolve(token, new Hits().HitTest(default, new()));
            return token;
        }
        public void Advance(int ms) { Clock.Now += TimeSpan.FromMilliseconds(ms); Engine.Tick(); }
        public int[] Steps => Effects.Where(e => e.Kind == GestureEffectKind.SwitchMaximized).Select(e => e.Steps).ToArray();
        public string?[] Reports => Effects.Where(e => e.Kind == GestureEffectKind.RightWheelReport).Select(e => e.Text).ToArray();
        public void AssertNoClickOrCommand() => Assert.DoesNotContain(Effects,
            e => e.Kind is GestureEffectKind.Replay or GestureEffectKind.Command);
    }

    [Theory]
    [InlineData(MouseEventKind.Wheel, -120, false, 1)]
    [InlineData(MouseEventKind.Wheel, 120, false, -1)]
    [InlineData(MouseEventKind.Wheel, -120, true, 1)]
    [InlineData(MouseEventKind.HorizontalWheel, 120, false, 1)]
    [InlineData(MouseEventKind.HorizontalWheel, -120, false, -1)]
    [InlineData(MouseEventKind.HorizontalWheel, 120, true, -1)]
    [InlineData(MouseEventKind.HorizontalWheel, -120, true, 1)]
    public void DirectionsConsumeWheelAndReleaseWithoutClick(MouseEventKind kind, int delta, bool reverse, int steps)
    {
        var h = new H(reverse: reverse); h.Down();
        Assert.True(h.Event(kind, delta)); Assert.Equal(new[] { steps }, h.Steps);
        Assert.True(h.Event(MouseEventKind.Up)); h.AssertNoClickOrCommand();
        Assert.Single(h.Reports);
        Assert.False(h.Event(MouseEventKind.Up)); Assert.Single(h.Reports);
    }

    [Theory]
    [InlineData(MouseEventKind.Wheel)]
    [InlineData(MouseEventKind.HorizontalWheel)]
    public void AccumulatesConfiguredStepAndResetsOnReversal(MouseEventKind kind)
    {
        var h = new H(step: 240); h.Down();
        Assert.True(h.Event(kind, -120)); Assert.Empty(h.Steps);
        Assert.True(h.Event(kind, 120)); Assert.Empty(h.Steps);
        Assert.True(h.Event(kind, 120));
        Assert.Equal(new[] { kind == MouseEventKind.Wheel ? -1 : 1 }, h.Steps);
        Assert.True(h.Event(kind, -480));
        Assert.Equal(kind == MouseEventKind.Wheel ? 2 : -2, h.Steps[^1]);
        h.Event(MouseEventKind.Up); h.AssertNoClickOrCommand();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallWheelCancelsClickAndExistingGestureIncludingTooltip(bool stroke)
    {
        var h = new H(step: 240); h.Down();
        if (stroke)
        {
            h.Event(MouseEventKind.Move, x: -20); h.Advance(1000);
            Assert.Contains(h.Effects, e => e.Kind == GestureEffectKind.Tooltip && e.Text != null);
        }
        int before = h.Effects.Count;
        Assert.True(h.Event(MouseEventKind.Wheel, -1)); Assert.Empty(h.Steps);
        Assert.Contains(h.Effects.Skip(before), e => e.Kind == GestureEffectKind.TrailEnd);
        h.Event(MouseEventKind.Move, x: -50); h.Advance(4000);
        Assert.DoesNotContain(h.Effects.Skip(before), e => e.Kind is GestureEffectKind.TrailShow or GestureEffectKind.Tooltip);
        Assert.True(h.Event(MouseEventKind.Up)); h.AssertNoClickOrCommand();
        Assert.Contains("次へ 0・前へ 0", Assert.Single(h.Reports)!);
    }

    [Fact]
    public void PendingHitAndRapidFireCannotExecuteAfterWheel()
    {
        var h = new H(); long token = h.Down(resolve: false);
        h.Event(MouseEventKind.Move, x: -20);
        Assert.True(h.Event(MouseEventKind.Down, button: PointerButton.Left));
        Assert.True(h.Event(MouseEventKind.Wheel, -120));
        Assert.True(h.Event(MouseEventKind.Up));
        h.Engine.Resolve(token, new(42, true, "CloseWindow"));
        Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Left));
        h.Advance(3000); h.AssertNoClickOrCommand(); Assert.Equal(new[] { 1 }, h.Steps);
    }

    [Fact]
    public void RightWheelTakesPriorityOverFastScrollAndFocus()
    {
        var h = new H(); h.Down();
        h.Engine.SetCache(new Hits().Probe(default, h.Clock.Now));
        h.Effects.Clear(); Assert.True(h.Event(MouseEventKind.Wheel, -120));
        Assert.DoesNotContain(h.Effects, e => e.Kind is GestureEffectKind.Focus or GestureEffectKind.Probe or GestureEffectKind.Replay);
        Assert.Equal(new[] { 1 }, h.Steps);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void OffOrNoRightPressPreservesFastScrollAndFocus(bool enabled, bool down)
    {
        var h = new H(enabled: enabled); if (down) h.Down();
        h.Engine.SetCache(new Hits().Probe(default, h.Clock.Now));
        Assert.True(h.Event(MouseEventKind.Wheel, -120));
        Assert.Equal(-600, Assert.Single(h.Effects, e => e.Kind == GestureEffectKind.Replay).Input.Delta);
        Assert.Contains(h.Effects, e => e.Kind == GestureEffectKind.Focus);
        Assert.False(h.Event(MouseEventKind.HorizontalWheel, 120)); Assert.Empty(h.Steps);
        Assert.Empty(h.Reports);
    }

    [Fact]
    public void DisabledGestureNeverStartsRightWheel()
    {
        var h = new H(gesture: false);
        Assert.False(h.Event(MouseEventKind.Down)); Assert.False(h.Event(MouseEventKind.Wheel, -120));
        Assert.False(h.Event(MouseEventKind.Up)); Assert.Empty(h.Steps); Assert.Empty(h.Reports);
    }

    [Fact]
    public void PausePassesNewInputAndStopsExistingWheelUntilRelease()
    {
        var h = new H(); h.Engine.SetPaused(true);
        Assert.False(h.Event(MouseEventKind.Down)); Assert.False(h.Event(MouseEventKind.Wheel, -120));
        Assert.Empty(h.Steps);
        h.Engine.SetPaused(false); h.Down(); h.Event(MouseEventKind.Wheel, -120);
        h.Engine.SetPaused(true); Assert.False(h.Event(MouseEventKind.HorizontalWheel, 120));
        h.Engine.SetPaused(false); Assert.False(h.Event(MouseEventKind.HorizontalWheel, 120));
        Assert.True(h.Event(MouseEventKind.Up)); h.AssertNoClickOrCommand();
        Assert.Equal(new[] { 1 }, h.Steps); Assert.Single(h.Reports);
    }

    [Fact]
    public void HoldHasNoKeySelectionTimeoutAndNewPressResetsRemainders()
    {
        var h = new H(step: 240, ms: 0); h.Down(); h.Advance(5000);
        Assert.True(h.Event(MouseEventKind.Wheel, -120)); h.Advance(10000);
        Assert.True(h.Event(MouseEventKind.Wheel, -120)); Assert.Equal(new[] { 1 }, h.Steps);
        h.Event(MouseEventKind.Wheel, -120); h.Event(MouseEventKind.Up);
        h.Down(); h.Event(MouseEventKind.Wheel, -120); h.Event(MouseEventKind.Up);
        Assert.Equal(new[] { 1 }, h.Steps); Assert.Equal(2, h.Reports.Length); h.AssertNoClickOrCommand();
    }

    [Fact]
    public void InjectedOwnWheelDoesNotConsumeRightClick()
    {
        var h = new H(); h.Down();
        Assert.False(h.Engine.Handle(new(MouseEventKind.Wheel, default, Delta: -120, ExtraInfo: KeyStroke.ExtraInfo)));
        h.Event(MouseEventKind.Up); Assert.Empty(h.Steps); Assert.Empty(h.Reports);
        Assert.Equal(2, h.Effects.Count(e => e.Kind == GestureEffectKind.Replay));
    }

    [Fact]
    public void ReportsCountsAndTotalsOnceAtRelease()
    {
        var h = new H(); h.Down();
        for (int i = 0; i < 3; i++) h.Event(MouseEventKind.Wheel, -120);
        h.Event(MouseEventKind.HorizontalWheel, -240); Assert.Empty(h.Reports);
        h.Event(MouseEventKind.Up); h.Engine.Shutdown();
        Assert.Equal("右ボタン＋ホイールで切り替え：縦 3 回（合計 -360）、横 1 回（合計 -240）、次へ 3・前へ 2", Assert.Single(h.Reports));
    }

    [Fact]
    public void ExcludedHitKeepsReplayedButtonAndWheelBehavior()
    {
        var h = new H(); long token = h.Down(resolve: false);
        h.Engine.Resolve(token, new(42, false)); h.Engine.ReplayCompleted(token);
        Assert.False(h.Event(MouseEventKind.Wheel, -120)); Assert.False(h.Event(MouseEventKind.Up));
        Assert.Empty(h.Steps); Assert.Empty(h.Reports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoWheelKeepsClickOrGesture(bool stroke)
    {
        var h = new H(); h.Down();
        if (stroke) h.Event(MouseEventKind.Move, x: -20);
        Assert.True(h.Event(MouseEventKind.Up)); Assert.Empty(h.Steps); Assert.Empty(h.Reports);
        if (stroke)
        {
            Assert.Equal("Back", Assert.Single(h.Effects, e => e.Kind == GestureEffectKind.Command).Text);
            Assert.DoesNotContain(h.Effects, e => e.Kind == GestureEffectKind.Replay);
        }
        else Assert.Equal(2, h.Effects.Count(e => e.Kind == GestureEffectKind.Replay));
    }

    [Fact]
    public void AxesAccumulateSeparatelyAndReleaseStopsCapture()
    {
        var h = new H(); h.Down();
        h.Event(MouseEventKind.Wheel, -60); h.Event(MouseEventKind.HorizontalWheel, 60);
        Assert.Empty(h.Steps);
        h.Event(MouseEventKind.Wheel, -60); h.Event(MouseEventKind.HorizontalWheel, 60);
        Assert.Equal(new[] { 1, 1 }, h.Steps); h.Event(MouseEventKind.Up);
        Assert.False(h.Event(MouseEventKind.Wheel, -120));
        Assert.Equal(new[] { 1, 1 }, h.Steps); Assert.Single(h.Reports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HookLossOrMissingReleaseEndsOldSummaryAndResetsAccumulation(bool hookLost)
    {
        var h = new H(); h.Down(); h.Event(MouseEventKind.Wheel, -60);
        if (hookLost) h.Engine.HookLost();
        h.Down(); Assert.Single(h.Reports);
        h.Event(MouseEventKind.Wheel, -60); Assert.Empty(h.Steps);
        h.Event(MouseEventKind.Up); Assert.Equal(2, h.Reports.Length); h.AssertNoClickOrCommand();
    }

    [Fact]
    public void SettingsReloadDoesNotReplayConsumedRightPress()
    {
        var h = new H(); h.Down(); h.Event(MouseEventKind.Wheel, -60);
        h.Engine.UpdateSettings(new());
        Assert.False(h.Event(MouseEventKind.Wheel, -120));
        Assert.True(h.Event(MouseEventKind.Up)); h.AssertNoClickOrCommand();
        Assert.Empty(h.Steps); Assert.Single(h.Reports);
    }
}
