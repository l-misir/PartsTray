// ボタン保持中の選択と、解放・時間切れ・中断時の状態を偽時計で検証します。
// ボタンとホイールの組み合わせや安全終了を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class HoldButtonSelectionTests
{
    private readonly FakeClock clock = new();
    private readonly WindowSwitchWheel wheel;
    private readonly WindowHoldSelection hold;

    public HoldButtonSelectionTests()
    {
        wheel = new(() => clock.Now);
        hold = new(wheel, () => clock.Now);
        hold.Configure(new() { HoldButton = "0x00C4", WheelSwitchMs = 0 });
    }

    [Fact]
    public void PressAloneDoesNotSwitchAndOnlyHeldWheelCounts()
    {
        Assert.False(wheel.Receive(false, -120).Handled);
        Assert.Equal(HoldButtonChange.Started, hold.ReceiveButtons([0xC4]));
        Assert.Equal(default, wheel.Summary);
        Assert.Equal(1, wheel.Receive(false, -120).Steps);
        Assert.Equal(-1, wheel.Receive(false, 120).Steps);
        Assert.Equal(HoldButtonChange.Ended, hold.ReceiveButtons([]));
        var summary = wheel.Summary;
        Assert.False(wheel.Receive(false, -120).Handled);
        Assert.False(wheel.ReceiveDeviceRotation(6).Handled);
        Assert.Equal(summary, wheel.Summary);
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([]));
    }

    [Fact]
    public void OtherButtonsDoNotStartOrResetSelection()
    {
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0x53]));
        Assert.Equal(HoldButtonChange.Started, hold.ReceiveButtons([0x53, 0xC4]));
        Assert.Equal(0, wheel.Receive(false, -60).Steps);
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4, 0x56]));
        Assert.Equal(1, wheel.Receive(false, -60).Steps);
        Assert.Equal(HoldButtonChange.Ended, hold.ReceiveButtons([0x56]));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(1500)] [InlineData(5000)]
    public void FixedThirtySecondsIgnoresKeyTimeoutAndRepeatedInput(int milliseconds)
    {
        hold.Configure(new() { HoldButton = "0x00C4", WheelSwitchMs = milliseconds });
        hold.ReceiveButtons([0xC4]);
        clock.Now = TimeSpan.FromMilliseconds(29999);
        Assert.False(hold.Expire());
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4]));
        Assert.Equal(1, wheel.Receive(false, -120).Steps);
        clock.Now = TimeSpan.FromSeconds(30);
        Assert.True(hold.Expire());
        Assert.False(hold.IsActive);
        Assert.False(wheel.Receive(false, -120).Handled);
        Assert.False(hold.Expire());
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4]));
        hold.ReceiveButtons([]);
        Assert.Equal(HoldButtonChange.Started, hold.ReceiveButtons([0xC4]));
    }

    [Fact]
    public void NotificationAlsoChecksDeadline()
    {
        hold.ReceiveButtons([0xC4]);
        clock.Now = TimeSpan.FromSeconds(31);
        Assert.Equal(HoldButtonChange.Ended, hold.ReceiveButtons([0xC4]));
        Assert.False(wheel.IsActive);
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4]));
    }

    [Fact]
    public void EmptySettingDoesNothingAndDoesNotStopKeySelection()
    {
        hold.Configure(new());
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4, 0xC3, 0x53, 0x1A0]));
        Assert.False(wheel.IsActive);
        wheel.Start(new());
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4]));
        Assert.False(hold.Stop());
        Assert.True(wheel.IsActive);
    }

    [Fact]
    public void CancellationWaitsForReleaseAndNewPressStartsWithNoRemainder()
    {
        hold.ReceiveButtons([0xC4]);
        wheel.Receive(false, -60);
        Assert.True(hold.Stop());
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4]));
        hold.ReceiveButtons([]);
        hold.ReceiveButtons([0xC4]);
        Assert.Equal(0, wheel.Receive(false, -60).Steps);
        Assert.Equal(1, wheel.Receive(false, -60).Steps);
    }

    [Fact]
    public void SettingsChangeStopsAndSelectsNewButton()
    {
        hold.ReceiveButtons([0xC4]);
        hold.Configure(new() { HoldButton = "0x0053" });
        Assert.False(wheel.IsActive);
        Assert.Equal(HoldButtonChange.None, hold.ReceiveButtons([0xC4]));
        Assert.Equal(HoldButtonChange.Started, hold.ReceiveButtons([0x53]));
        hold.Configure(new());
        Assert.False(hold.IsActive);
        Assert.False(wheel.IsActive);
    }

    [Theory]
    [InlineData(false, 1)] [InlineData(true, -1)]
    public void DeviceUsesSharedStepDirectionAndDoesNotCountFollowingHookAgain(bool reverse, int direction)
    {
        hold.Configure(new() { HoldButton = "0x00C4", WheelSwitchStep = 240, ReverseHorizontal = reverse });
        hold.ReceiveButtons([0xC4]);
        Assert.Equal(0, wheel.ReceiveDeviceRotation(6).Steps);
        Assert.Equal(direction, wheel.ReceiveDeviceRotation(6).Steps);
        var summary = wheel.Summary;
        Assert.Equal(new WindowSwitchWheelResult(true, 0), wheel.Receive(true, 240));
        Assert.Equal(summary, wheel.Summary);
        Assert.Equal(1, wheel.Receive(false, -240).Steps);
    }

    [Fact]
    public void DisabledDeviceAndOwnInputDoNotCount()
    {
        hold.Configure(new() { HoldButton = "0x00C4", ThumbWheelFromDevice = false });
        hold.ReceiveButtons([0xC4]);
        Assert.False(wheel.ReceiveDeviceRotation(6).Handled);
        Assert.False(wheel.Receive(false, -120, KeyStroke.ExtraInfo).Handled);
        Assert.Equal(default, wheel.Summary);
        Assert.Equal(1, wheel.Receive(true, 120).Steps);
    }
}
