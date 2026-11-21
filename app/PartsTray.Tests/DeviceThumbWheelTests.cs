// HID++ 回転の選択時間・押下中の計数、設定、二重計数防止を検証します。
// サムホイールの換算や入力源の優先順位を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DeviceThumbWheelTests
{
    [Theory]
    [InlineData(false, 6, 1)] [InlineData(false, -6, -1)]
    [InlineData(true, 6, -1)] [InlineData(true, -6, 1)]
    public void UsesNormalConverterAndConfiguredDirection(bool reverse, short raw, int expected)
    {
        var wheel = new WindowSwitchWheel(() => TimeSpan.Zero);
        wheel.Start(new() { ReverseHorizontal = reverse });
        Assert.Equal(new WindowSwitchWheelResult(true, expected), wheel.ReceiveDeviceRotation(raw));
        Assert.Equal(1, wheel.Summary.HorizontalCount);
        Assert.Equal(raw * 20, wheel.Summary.HorizontalDelta);
        Assert.Equal(raw, wheel.Summary.DeviceRotationTotal);
        Assert.Contains($"生の回転量合計 {raw}", wheel.Summary.ToString());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DeviceSourceSuppressesHookWithoutCountingAndResetsNextSession(bool untilRelease)
    {
        var wheel = new WindowSwitchWheel(() => TimeSpan.Zero);
        var settings = new WindowKeySettings { WheelSwitchStep = 240 };
        wheel.Start(settings, untilRelease);
        Assert.Equal(0, wheel.Receive(true, 120).Steps);
        Assert.Equal(0, wheel.ReceiveDeviceRotation(6).Steps); // 最初の HID でフックの余りを捨てる。
        var summary = wheel.Summary;
        Assert.Equal(new WindowSwitchWheelResult(true, 0), wheel.Receive(true, 240));
        Assert.Equal(summary, wheel.Summary);
        Assert.Equal(1, wheel.ReceiveDeviceRotation(6).Steps);
        Assert.Equal(1, wheel.Receive(false, -240).Steps); // 縦は影響を受けない。
        Assert.Equal(12, wheel.Summary.DeviceRotationTotal);
        wheel.Stop();
        Assert.False(wheel.ReceiveDeviceRotation(6).Handled);
        wheel.Start(settings, untilRelease);
        Assert.Equal(default, wheel.Summary);
        Assert.Equal(1, wheel.Receive(true, 240).Steps);
    }

    [Fact]
    public void DeviceEventsOutsideSelectionOrDisabledNeverCount()
    {
        var wheel = new WindowSwitchWheel(() => TimeSpan.Zero);
        Assert.False(wheel.ReceiveDeviceRotation(6).Handled);
        wheel.Start(new() { ThumbWheelFromDevice = false });
        Assert.False(wheel.ReceiveDeviceRotation(6).Handled);
        Assert.Equal(default, wheel.Summary);
        Assert.Equal(1, wheel.Receive(true, 120).Steps);
        wheel.Start(new() { WheelSwitchMs = 0 });
        Assert.False(wheel.ReceiveDeviceRotation(6).Handled);
    }

    [Fact]
    public void DeviceExtendsSelectionButNeverRevivesExpiredSelection()
    {
        TimeSpan now = default;
        var wheel = new WindowSwitchWheel(() => now);
        wheel.Start(new());
        now = TimeSpan.FromMilliseconds(1499);
        Assert.True(wheel.ReceiveDeviceRotation(1).Handled);
        now = TimeSpan.FromMilliseconds(1500);
        Assert.False(wheel.Expire());
        now = TimeSpan.FromMilliseconds(2999);
        Assert.False(wheel.ReceiveDeviceRotation(6).Handled);
        Assert.Equal(1, wheel.Summary.DeviceRotationTotal);
    }

    private sealed class Effects : IGestureEffects
    {
        public readonly List<GestureEffect> Items = [];
        public void Post(GestureEffect e) => Items.Add(e);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RightPressCountsDeviceAndBlocksHookUntilRelease(bool resolved)
    {
        var effects = new Effects(); var clock = new FakeClock();
        var engine = new GestureEngine(clock, effects, new()
        { Gesture = new() { Enabled = true, RightWheelSwitch = true } });
        Assert.False(engine.ReceiveDeviceRotation(6));
        engine.Handle(new(MouseEventKind.Down, default, PointerButton.Right));
        long token = effects.Items.Last(e => e.Kind == GestureEffectKind.HitTest).Token;
        if (resolved) engine.Resolve(token, new(42, true));
        Assert.True(engine.ReceiveDeviceRotation(3));
        Assert.True(engine.Handle(new(MouseEventKind.HorizontalWheel, default, Delta: 120)));
        Assert.DoesNotContain(effects.Items, e => e.Kind == GestureEffectKind.SwitchMaximized);
        Assert.True(engine.ReceiveDeviceRotation(3));
        Assert.Equal(1, Assert.Single(effects.Items, e => e.Kind == GestureEffectKind.SwitchMaximized).Steps);
        engine.Resolve(token, new(42, true, "CloseWindow"));
        engine.Handle(new(MouseEventKind.Up, default, PointerButton.Right));
        Assert.False(engine.ReceiveDeviceRotation(6));
        var report = Assert.Single(effects.Items, e => e.Kind == GestureEffectKind.RightWheelReport).Text!;
        Assert.Contains("横 2 回（合計 120）", report);
        Assert.Contains("生の回転量合計 6", report);
        Assert.DoesNotContain(effects.Items, e => e.Kind is GestureEffectKind.Replay or GestureEffectKind.Command);
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void DeviceRequiresAllSettingsAndUnpausedEngine(bool device, bool rightWheel, bool gesture, bool paused)
    {
        var effects = new Effects();
        var engine = new GestureEngine(new FakeClock(), effects, new()
        {
            Gesture = new() { Enabled = gesture, RightWheelSwitch = rightWheel },
            WindowKeys = new() { ThumbWheelFromDevice = device }
        });
        engine.Handle(new(MouseEventKind.Down, default, PointerButton.Right));
        if (paused) engine.SetPaused(true);
        Assert.False(engine.ReceiveDeviceRotation(6));
        Assert.DoesNotContain(effects.Items, e => e.Kind == GestureEffectKind.SwitchMaximized);
    }
}
