// GapSession のサムホイール通知と、肩代わり中の従来送信を偽の機器で検証します。
// Options+ への引き継ぎ後の通知条件や、Active の送信を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class GapThumbNotificationTests
{
    [Fact]
    public void RetreatedPublishesEverySignedRotationWithoutSendingInput()
    {
        var h = new Harness(); h.Activate(); h.Options(19, 2, 1, 1);
        Assert.Equal(SessionState.Retreated, h.Session.State);
        var rotations = new List<short>(); h.Session.ThumbWheelRotated += rotations.Add;
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 6));
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 255, 250));
        h.Session.Pump();
        Assert.Equal(new short[] { 6, -6 }, rotations);
        Assert.Empty(h.Input.WheelEvents);
        Assert.Single(h.Log.Lines, l => l.Contains("サムホイール回転を検出"));
    }

    [Theory]
    [InlineData("divert")] [InlineData("hook")] [InlineData("off")]
    public void ActiveKeepsItsExistingWheelPath(string mode)
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true,
            ThumbWheel = new() { Mode = mode, SpeedPercent = 100, DirectionSign = 1 } } });
        h.Activate();
        var rotations = new List<short>(); h.Session.ThumbWheelRotated += rotations.Add;
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 6)); h.Session.Pump();
        Assert.Empty(rotations);
        if (mode == "divert") Assert.Equal(new[] { 120 }, h.Input.WheelEvents);
        else Assert.Empty(h.Input.WheelEvents);
    }

    [Fact]
    public void IgnoresWrongDeviceFeatureFunctionResponseZeroAndDisconnectedReports()
    {
        var h = new Harness(); h.Activate(); h.Options(19, 2, 1, 1);
        var rotations = new List<short>(); h.Session.ThumbWheelRotated += rotations.Add;
        h.Session.Receive([0x10, 2, 19, 0, 0, 6, 0]); // 不完全な短いレポート
        h.Session.Receive(Harness.Frame(1, 19, 0, 0, 0, 6));
        h.Session.Receive(Harness.Frame(2, 20, 0, 0, 0, 6));
        h.Session.Receive(Harness.Frame(2, 19, 1, 0, 0, 6));
        h.Session.Receive(Harness.Frame(2, 19, 0, 5, 0, 6));
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 0));
        h.Session.ReceiverLost();
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 6));
        Assert.Empty(rotations);
    }

    [Fact]
    public void PausedSessionDoesNotPublishDeviceInput()
    {
        var h = new Harness(); h.Activate(); h.Options(19, 2, 1, 1);
        var rotations = new List<short>(); h.Session.ThumbWheelRotated += rotations.Add;
        h.Session.SetPaused(true);
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 6));
        Assert.Empty(rotations);
    }
}
