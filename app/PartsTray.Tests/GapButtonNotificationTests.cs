// 肩代わり中・引き継ぎ後のボタン通知を偽の HID レポートで検証します。
// 通知の対象・完全性・従来の肩代わりとの共存を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class GapButtonNotificationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PublishesAllFourCidsEveryTimeIncludingRelease(bool retreated)
    {
        var h = new Harness(); h.Activate();
        if (retreated) h.Options(19, 2, 1, 1);
        var notifications = new List<IReadOnlyList<ushort>>();
        h.Session.PressedButtonsChanged += notifications.Add;
        h.Buttons(0xC4, 0x53, 0x1A0, 0xFFFF);
        h.Buttons(0xC4, 0x53, 0x1A0, 0xFFFF);
        h.Buttons(); h.Session.Pump();
        Assert.Equal(3, notifications.Count);
        Assert.Equal(new ushort[] { 0xC4, 0x53, 0x1A0, 0xFFFF }, notifications[0]);
        Assert.Equal(notifications[0], notifications[1]);
        Assert.Empty(notifications[2]);
        Assert.Throws<NotSupportedException>(() => ((IList<ushort>)notifications[0])[0] = 1);
        if (retreated) Assert.Empty(h.Input.KeyEvents);
        else Assert.Equal(new[] { "Ctrl+V", "Win+Up" }, h.Input.KeyEvents);
    }

    [Fact]
    public void IgnoresZeroDuplicatesInvalidFramesAndDisconnectedDevice()
    {
        var h = new Harness(); h.Activate();
        var notifications = new List<IReadOnlyList<ushort>>();
        h.Session.PressedButtonsChanged += notifications.Add;
        h.Session.Receive([0x10, 2, 13, 0, 0, 0xC4, 0]);
        h.Session.Receive(Harness.Frame(1, 13, 0, 0, 0, 0xC4));
        h.Session.Receive(Harness.Frame(2, 20, 0, 0, 0, 0xC4));
        h.Session.Receive(Harness.Frame(2, 13, 1, 0, 0, 0xC4));
        h.Session.Receive(Harness.Frame(2, 13, 0, 5, 0, 0xC4));
        Assert.Empty(notifications);
        h.Buttons(0, 0xC4, 0xC4, 0);
        Assert.Equal(new ushort[] { 0xC4 }, Assert.Single(notifications));
        h.Session.ReceiverLost(); h.Buttons(0xC4);
        Assert.Single(notifications);
    }

    [Fact]
    public void SubscriberFailureDoesNotBreakOwnedButtonAction()
    {
        var h = new Harness(); h.Activate();
        h.Session.PressedButtonsChanged += _ => throw new InvalidOperationException("test");
        h.Buttons(0x56); h.Session.Pump();
        Assert.Equal(new[] { "Ctrl+C" }, h.Input.KeyEvents);
    }

    [Fact]
    public void DisabledGapCanListenWithoutAnyWritesOrInput()
    {
        var h = new Harness(new() { WindowKeys = new() { HoldButton = "0x00C4" } });
        var notifications = new List<IReadOnlyList<ushort>>();
        var rotations = new List<short>();
        h.Session.PressedButtonsChanged += notifications.Add;
        h.Session.ThumbWheelRotated += rotations.Add;
        h.Activate(); h.Buttons(0xC4, 0x53); h.Buttons();
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 6)); h.Session.Pump();
        Assert.Equal(SessionState.Paused, h.Session.State);
        Assert.Equal(2, notifications.Count);
        Assert.Equal(new short[] { 6 }, rotations);
        Assert.Empty(h.Writes); Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents);
        Assert.All(h.Sent, raw => Assert.Equal((byte)0, HidReport.Parse(raw)!.Feature));
    }

    [Fact]
    public void DisabledGapIdentifiesExistingMouseBeforeListening()
    {
        var h = new Harness(new() { WindowKeys = new() { HoldButton = "0x00C4" } });
        h.Names[2] = "MX Master 4";
        h.Session.ProbeExisting(); h.Session.Pump();
        var notifications = new List<IReadOnlyList<ushort>>();
        h.Session.PressedButtonsChanged += notifications.Add;
        h.Buttons(0xC4);
        Assert.Single(notifications); Assert.Empty(h.Writes);
    }

    [Fact]
    public void EnablingAndDisablingHoldUpdatesPassiveListener()
    {
        var h = new Harness(new()); h.Activate();
        Assert.Empty(h.Sent);
        var notifications = new List<IReadOnlyList<ushort>>();
        h.Session.PressedButtonsChanged += notifications.Add;
        h.Session.UpdateSettings(new() { WindowKeys = new() { HoldButton = "0x00C4" } });
        h.Session.Pump(); h.Buttons(0xC4);
        Assert.Single(notifications); Assert.Empty(h.Writes);
        h.Session.UpdateSettings(new()); h.Session.Pump(); h.Buttons();
        Assert.Single(notifications);
        Assert.Equal(Timeout.Infinite, h.Session.WorkerWaitMilliseconds);
    }
}
