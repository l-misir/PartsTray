using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class ReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupIdentifiesMouseWithoutConnectionNotification(bool shortReplies)
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }) { ShortNameReplies = shortReplies };
        h.Names[2] = "MX Master 4"; h.NameFeatures[2] = 7;
        h.Session.ProbeExisting(); h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(7, h.Writes.Length);
        Assert.All(h.Writes, r => Assert.Equal(2, r[1]));
        Assert.Contains(h.Sent, r => r[1] == 2 && r[2] == 0 && r[3] == 5 && r[4] == 0 && r[5] == 5);
        Assert.Equal(shortReplies ? new byte[] { 0, 3, 6, 9 } : new byte[] { 0 },
            h.Sent.Where(r => r[1] == 2 && r[2] == 7 && r[3] == 0x15).Select(r => r[4]));
    }

    [Fact]
    public void KeyboardGetsOnlyPingAndNameReadsThenScanContinues()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Names[3] = "MX Keys S"; h.NameFeatures[3] = 9;
        h.Names[4] = "MX Master 4";
        h.Session.ProbeExisting(); h.Session.Pump();
        var keyboard = h.Sent.Where(r => r[1] == 3).ToArray();
        Assert.Equal(4, keyboard.Length);
        Assert.Equal(new byte[] { 0, 0, 9, 9 }, keyboard.Select(r => r[2]));
        Assert.Equal(new byte[] { 0x15, 5, 5, 0x15 }, keyboard.Select(r => r[3]));
        Assert.Equal(new byte[] { 0, 5 }, keyboard[1][4..6]);
        Assert.Equal(7, h.Writes.Length); Assert.All(h.Writes, r => Assert.Equal(4, r[1]));
    }

    [Theory]
    [InlineData("MX Master 4S")]
    [InlineData("mx master 4")]
    [InlineData("MX Master 4\0")]
    public void StartupRequiresExactMouseName(string name)
    {
        var h = new Harness(); h.Names[2] = name;
        h.Session.ProbeExisting(); h.Session.Pump();
        Assert.Empty(h.Writes); Assert.Equal(SessionState.Disconnected, h.Session.State);
    }

    [Fact]
    public void PingTimeoutAdvancesToNextSlotWithoutUnsolicitedTraffic()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Names[2] = "MX Master 4";
        h.Intercept = r => r[1] == 1;
        h.Session.ProbeExisting(); h.Session.Pump();
        for (int i = 0; i < 4; i++) h.Advance(1);
        Assert.Equal(1, h.Session.WorkerWaitMilliseconds);
        Assert.All(h.Sent.Where(r => r[1] == 1), r => { Assert.Equal(0, r[2]); Assert.Equal(0x15, r[3]); });
        h.Session.Pump(); Assert.Equal(SessionState.Active, h.Session.State); Assert.Equal(7, h.Writes.Length);
    }

    [Fact]
    public void NameReadFailureMovesOnWithoutAdoptingUnknownDevice()
    {
        var h = new Harness(); h.Names[1] = "MX Master 4"; h.Names[2] = "MX Master 4";
        h.Intercept = r =>
        {
            if (r[1] != 1 || r[2] != 3) return false;
            h.Session.Receive([0x10, 1, 0x8F, r[2], r[3], 9, 0]); return true;
        };
        h.Session.ProbeExisting(); h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State); Assert.All(h.Writes, r => Assert.Equal(2, r[1]));
    }

    [Theory]
    [InlineData(true, true, SessionState.Retreated)]
    [InlineData(true, false, SessionState.Active)]
    [InlineData(false, true, SessionState.Active)]
    public void EveryLinkChecksReportingBeforeActivation(bool options, bool diverted, SessionState expected)
    {
        var h = new Harness { OptionsRunning = options }; h.Cids[0x52] = diverted ? (byte)1 : (byte)0;
        h.Link(); Assert.Equal(SessionState.Retreated, h.Session.State);
        h.Buttons(0x56); h.Session.Pump(); Assert.Equal(expected, h.Session.State);
        int query = h.Sent.FindIndex(r => r[2] == 13 && r[3] == 0x25 && r[5] == 0x52);
        Assert.True(query >= 0);
        if (expected == SessionState.Retreated)
        {
            int count = h.Sent.Count;
            h.Buttons(0x56); h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 1)); h.Advance(30);
            Assert.Empty(h.Writes); Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents);
            Assert.Equal(count, h.Sent.Count); // No cleanup either.
        }
        else Assert.True(h.Sent.FindIndex(r => r[2] == 20 && r[3] == 0x35) > query);
    }

    [Fact]
    public void OptionsProcessIsReadAtDecisionOnEachConnection()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }) { OptionsRunning = false }; h.Cids[0x52] = 1;
        h.Intercept = r => { if (r[2] == 13 && r[3] == 0x25) h.OptionsRunning = true; return false; };
        h.Activate(); Assert.Equal(SessionState.Retreated, h.Session.State); Assert.Empty(h.Writes);
        h.Intercept = null; h.OptionsRunning = false; h.Link(); h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State); Assert.Equal(7, h.Writes.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviousActiveSessionResumesOwnDiversion(bool receiverRemoved)
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Session.ReceiverOpened("receiver-A"); h.Activate();
        if (receiverRemoved) { h.Session.ReceiverLost(); h.Session.ReceiverOpened("receiver-A"); }
        else h.Session.Receive([0x10, 2, 0x41, 0x10, 0x42, 0x42, 0xB0]);
        h.Sent.Clear(); h.Link(); h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State); Assert.Equal(7, h.Writes.Length);
        h.Buttons(0x56); h.Session.Pump(); Assert.Equal(new[] { "Ctrl+C" }, h.Input.KeyEvents);
    }

    [Theory]
    [InlineData("retreated")]
    [InlineData("other-receiver")]
    [InlineData("other-slot")]
    public void ActiveHistoryDoesNotOverrideOtherOwners(string scenario)
    {
        var h = new Harness(); h.Session.ReceiverOpened("receiver-A"); h.Activate();
        if (scenario == "retreated") h.Options();
        h.Session.ReceiverLost();
        h.Session.ReceiverOpened(scenario == "other-receiver" ? "receiver-B" : "receiver-A");
        h.Sent.Clear(); h.Link(scenario == "other-slot" ? (byte)4 : (byte)2); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State); Assert.Empty(h.Writes); h.Advance(30); Assert.Empty(h.Writes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void CleanupHonorsLatestOptionsOffIntent(bool previouslyOn, bool deviceStillOn)
    {
        var h = new Harness(); h.Activate(); h.Options();
        foreach (ushort c in GapSession.ButtonOrder.Where(c => c != 0x53)) h.Options(13, 3, (byte)(c >> 8), (byte)c, 0x23);
        h.Options(19, 2);
        if (previouslyOn) h.Options(13, 3, 0, 0x53, 0x23);
        h.Options(13, 3, 0, 0x53, 0x22);
        h.Cids[0x53] = deviceStillOn ? (byte)1 : (byte)0;
        h.Sent.Clear(); h.Advance(15);
        Assert.Contains(h.Sent, r => r[2] == 13 && r[3] == 0x25 && r[5] == 0x53);
        if (deviceStillOn) Assert.Equal(new byte[] { 0, 0x53, 2, 0, 0x53 }, Assert.Single(h.Writes)[4..9]);
        else Assert.Empty(h.Writes);
    }

    [Fact]
    public void LaterOptionsOnCancelsOffIntentCleanup()
    {
        var h = new Harness(); h.Activate(); h.Options();
        foreach (ushort c in GapSession.ButtonOrder) h.Options(13, 3, (byte)(c >> 8), (byte)c, 0x23);
        h.Options(19, 2); h.Options(13, 3, 0, 0x53, 0x22);
        h.Intercept = r =>
        {
            if (r[2] != 13 || r[3] != 0x25) return false;
            h.Reply(r); h.Options(13, 3, 0, 0x53, 0x23); return true;
        };
        h.Sent.Clear(); h.Advance(15); Assert.Empty(h.Writes);
    }
}
