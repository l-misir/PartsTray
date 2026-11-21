// ホイールで前後を選ぶ方向・蓄積量・期限・入力除外・集計を、OS に触れずに検証します。
// WindowSwitchWheel の動きや時間の扱いを変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WindowSwitchWheelTests
{
    private TimeSpan now;
    private WindowSwitchWheel Create(WindowKeySettings? settings = null)
    {
        var wheel = new WindowSwitchWheel(() => now);
        wheel.Start(settings ?? new());
        return wheel;
    }

    [Theory]
    [InlineData(false, -120, false, 1)]
    [InlineData(false, 120, false, -1)]
    [InlineData(true, 120, false, 1)]
    [InlineData(true, -120, false, -1)]
    [InlineData(true, 120, true, -1)]
    [InlineData(true, -120, true, 1)]
    [InlineData(false, -120, true, 1)]
    [InlineData(false, 120, true, -1)]
    public void UsesTheRequestedDirection(bool horizontal, int delta, bool reverse, int steps)
    {
        var wheel = Create(new() { ReverseHorizontal = reverse });
        Assert.Equal(new WindowSwitchWheelResult(true, steps), wheel.Receive(horizontal, delta));
    }

    [Theory]
    [InlineData(1)] [InlineData(75)] [InlineData(120)] [InlineData(1200)]
    public void WaitsForTheStepAndRetainsTheRemainder(int step)
    {
        var wheel = Create(new() { WheelSwitchStep = step });
        Assert.Equal(0, wheel.Receive(true, step - 1).Steps);
        Assert.Equal(3, wheel.Receive(true, 2 * step + 1).Steps);
        Assert.Equal(0, wheel.Receive(true, step - 1).Steps);
        Assert.Equal(1, wheel.Receive(true, 1).Steps);
    }

    [Fact]
    public void KeepsVerticalAndHorizontalRemaindersSeparate()
    {
        var wheel = Create();
        Assert.Equal(0, wheel.Receive(false, -90).Steps);
        Assert.Equal(0, wheel.Receive(true, 90).Steps);
        Assert.Equal(1, wheel.Receive(false, -30).Steps);
        Assert.Equal(1, wheel.Receive(true, 30).Steps);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DirectionChangeDiscardsOnlyThatAxesOldRemainder(bool horizontal)
    {
        var wheel = Create();
        wheel.Receive(!horizontal, 90);
        Assert.Equal(0, wheel.Receive(horizontal, 90).Steps);
        Assert.Equal(0, wheel.Receive(horizontal, -30).Steps);
        Assert.Equal(horizontal ? -1 : 1, wheel.Receive(horizontal, -90).Steps);
        Assert.Equal(horizontal ? -1 : 1, wheel.Receive(!horizontal, 30).Steps);
        // 余りが 0 の直後でも、新しい方向の最初の量を失わない。
        Assert.Equal(horizontal ? 1 : -1, wheel.Receive(horizontal, 120).Steps);
    }

    [Fact]
    public void EachWheelEventExtendsTimeEvenWithoutACompletedStep()
    {
        var wheel = Create();
        now = TimeSpan.FromMilliseconds(1499);
        Assert.Equal(new WindowSwitchWheelResult(true, 0), wheel.Receive(true, 1));
        now = TimeSpan.FromMilliseconds(1500);
        Assert.False(wheel.Expire());
        now = TimeSpan.FromMilliseconds(2998);
        Assert.False(wheel.Expire());
        now = TimeSpan.FromMilliseconds(2999);
        Assert.True(wheel.Expire());
        Assert.False(wheel.IsActive);
        Assert.False(wheel.Receive(true, 120).Handled);
    }

    [Fact]
    public void WheelAtDeadlinePassesThroughWithoutRestartingOrCounting()
    {
        var wheel = Create();
        now = TimeSpan.FromMilliseconds(1500);
        Assert.False(wheel.Receive(false, -120).Handled);
        Assert.False(wheel.IsActive);
        Assert.Equal(default, wheel.Summary);
        Assert.False(wheel.Expire());
    }

    [Fact]
    public void ButtonEndsSelectionAndTheNextStartHasNoLeftover()
    {
        var wheel = Create();
        wheel.Receive(true, 90);
        wheel.ButtonDown();
        Assert.False(wheel.IsActive);
        Assert.False(wheel.Receive(true, 120).Handled);
        Assert.Equal(1, wheel.Summary.HorizontalCount);
        Assert.True(wheel.Start(new()));
        Assert.Equal(default, wheel.Summary);
        Assert.Equal(0, wheel.Receive(true, 30).Steps);
    }

    [Fact]
    public void DisabledAndNeverStartedSessionsPassAllWheelEvents()
    {
        var wheel = new WindowSwitchWheel(() => now);
        Assert.False(wheel.Receive(true, 120).Handled);
        Assert.False(wheel.Start(new() { WheelSwitchMs = 0 }));
        Assert.False(wheel.IsActive);
        Assert.False(wheel.Receive(false, -120).Handled);
        Assert.Equal(default, wheel.Summary);
    }

    [Fact]
    public void OwnInputsNeverCountConsumeExtendOrEndSelection()
    {
        var wheel = Create();
        now = TimeSpan.FromMilliseconds(1499);
        Assert.False(wheel.Receive(true, 120, KeyStroke.ExtraInfo).Handled);
        wheel.ButtonDown(KeyStroke.ExtraInfo);
        Assert.True(wheel.IsActive);
        Assert.Equal(default, wheel.Summary);
        now = TimeSpan.FromMilliseconds(1500);
        Assert.True(wheel.Expire());
    }

    [Fact]
    public void ExternalInputMarkersAreAcceptedAndSummaryKeepsRawDeltas()
    {
        var wheel = Create(new() { ReverseHorizontal = true });
        wheel.Receive(false, -240, 123);
        wheel.Receive(false, 60, 123);
        wheel.Receive(true, 180, 456);
        wheel.Receive(true, -120, 456);
        wheel.Stop();
        Assert.Equal(new WindowSwitchWheelSummary(2, -180, 2, 60, 3, 1), wheel.Summary);
        Assert.Equal("ホイールで切り替え：縦 2 回（合計 -180）、横 2 回（合計 60）、次へ 3・前へ 1", wheel.Summary.ToString());
        Assert.False(wheel.Receive(true, 120, 123).Handled);
        Assert.False(wheel.Stop());
    }
}
