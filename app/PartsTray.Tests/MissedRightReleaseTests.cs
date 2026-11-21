// よそで離された右ボタンからクリックや命令が復活しないことを検証します。
// 解放取りこぼしの回復や遅れて届くヒットテストの扱いを変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class MissedRightReleaseTests : IGestureEffects
{
    private readonly FakeClock clock = new();
    private readonly List<GestureEffect> effects = [];
    void IGestureEffects.Post(GestureEffect effect) => effects.Add(effect);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExternalReleaseNeverReplaysOrExecutesAndPassesLaterWheel(bool resolved, bool wheel)
    {
        var engine = new GestureEngine(clock, this, new() { Gesture = new() { Enabled = true, RightWheelSwitch = true } });
        Assert.True(engine.Handle(new(MouseEventKind.Down, default, PointerButton.Right)));
        long token = effects.Last(e => e.Kind == GestureEffectKind.HitTest).Token;
        if (resolved) engine.Resolve(token, new(42, true));
        engine.Handle(new(MouseEventKind.Move, new(-30, 0)));
        if (wheel) Assert.True(engine.Handle(new(MouseEventKind.HorizontalWheel, default, Delta: 60)));
        Assert.True(engine.IsRightButtonDown);
        effects.Clear();
        engine.RightButtonReleasedElsewhere();
        Assert.False(engine.IsRightButtonDown);
        Assert.Equal(0, engine.RightPressToken);
        Assert.Contains(effects, e => e.Kind == GestureEffectKind.TrailEnd);
        Assert.Contains(effects, e => e.Kind == GestureEffectKind.Tooltip && e.Text == null);
        Assert.Equal(wheel ? 1 : 0, effects.Count(e => e.Kind == GestureEffectKind.RightWheelReport));
        engine.RightButtonReleasedElsewhere();
        clock.Now += TimeSpan.FromSeconds(20);
        engine.Resolve(token, new(42, true, "CloseWindow"));
        engine.Tick();
        Assert.False(engine.Handle(new(MouseEventKind.Wheel, default, Delta: -120)));
        Assert.False(engine.Handle(new(MouseEventKind.HorizontalWheel, default, Delta: 120)));
        Assert.False(engine.Handle(new(MouseEventKind.Up, default, PointerButton.Right)));
        engine.Shutdown();
        Assert.DoesNotContain(effects, e => e.Kind is GestureEffectKind.Command or GestureEffectKind.Replay or GestureEffectKind.SwitchMaximized);
        Assert.Equal(wheel ? 1 : 0, effects.Count(e => e.Kind == GestureEffectKind.RightWheelReport));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AlreadyReplayedPressDoesNotSendAnotherRelease(bool replayCompleted)
    {
        var engine = new GestureEngine(clock, this, new() { Gesture = new() { Enabled = true } });
        engine.Handle(new(MouseEventKind.Down, default, PointerButton.Right));
        long token = effects.Last(e => e.Kind == GestureEffectKind.HitTest).Token;
        engine.Resolve(token, new(42, false));
        if (replayCompleted) engine.ReplayCompleted(token);
        effects.Clear();
        engine.RightButtonReleasedElsewhere();
        engine.ReplayCompleted(token);
        Assert.False(engine.Handle(new(MouseEventKind.Up, default, PointerButton.Right)));
        engine.Shutdown();
        Assert.DoesNotContain(effects, e => e.Kind is GestureEffectKind.Replay or GestureEffectKind.Command);
    }

    [Fact]
    public void PendingRapidFireChildCannotReviveAfterExternalRelease()
    {
        var engine = new GestureEngine(clock, this, new() { Gesture = new() { Enabled = true, RapidFire = true } });
        engine.Handle(new(MouseEventKind.Down, default, PointerButton.Right));
        long token = effects.Last(e => e.Kind == GestureEffectKind.HitTest).Token;
        engine.Handle(new(MouseEventKind.Move, new(-30, 0)));
        Assert.True(engine.Handle(new(MouseEventKind.Down, new(-30, 0), PointerButton.Left)));
        engine.RightButtonReleasedElsewhere();
        engine.Resolve(token, new(42, true));
        Assert.True(engine.Handle(new(MouseEventKind.Up, default, PointerButton.Left)));
        Assert.DoesNotContain(effects, e => e.Kind is GestureEffectKind.Command or GestureEffectKind.Replay);
    }
}
