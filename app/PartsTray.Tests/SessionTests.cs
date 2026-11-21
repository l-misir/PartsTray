// 肩代わりの適用・入力・引き継ぎを検証します。
// 対応する機能や入力条件を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class SessionTests
{
    [Fact] public void AppliesInRequiredOrderAndReadsThumbStatus()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Activate();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(7, h.Writes.Length);
        Assert.Equal(new byte[] { 0, 8, 0x98 }, h.Writes[0][4..7]);
        Assert.Equal(new ushort[] { 0x52, 0x56, 0x53, 0xC3, 0x1A0 }, h.Writes.Skip(1).Take(5).Select(r => HidReport.U16(r.AsSpan(4, 2))));
        Assert.All(h.Writes.Skip(1).Take(5), r => Assert.Equal(3, r[6]));
        Assert.Equal(new byte[] { 1, 1 }, h.Writes[6][4..6]);
        Assert.Equal(0x15, h.Sent[^1][3]);
        Assert.Contains(h.Log.Lines, l => l.Contains("適用開始 link+0.000ms"));
    }
    [Fact] public void ButtonsUseEdgesAndSimultaneousPresses()
    {
        var h = new Harness(); h.Activate();
        h.Buttons(0x56, 0x53); h.Session.Pump(); h.Buttons(0x56, 0x53); h.Session.Pump();
        h.Buttons(); h.Session.Pump();
        Assert.Equal(new[] { "Ctrl+C", "Ctrl+V" }, h.Input.KeyEvents);
        h.Buttons(0xC3, 0x1A0); h.Session.Pump();
        Assert.Equal(new[] { "PrintScreen", "Win+Up" }, h.Input.KeyEvents.Skip(2));
    }
    [Fact] public void WindowButtonsSendTheWindowKeysAndFollowKeyChanges()
    {
        var buttons = new Dictionary<string, ButtonSettings> { ["0x0056"] = new() { Action = "Maximize" }, ["0x0053"] = new() { Action = "NextMaximized" } };
        var config = new AppSettings { GapFiller = new() { Enabled = true, Buttons = buttons }, WindowKeys = new() { Maximize = "Ctrl+Alt+Shift+F10" } };
        var h = new Harness(config); h.Activate();
        h.Buttons(0x56); h.Session.Pump(); h.Buttons(); h.Session.Pump();
        h.Buttons(0x53); h.Session.Pump(); h.Buttons(); h.Session.Pump();   // 切り替えのキーは空なので送らない
        Assert.Equal(new[] { "Ctrl+Alt+Shift+F10" }, h.Input.KeyEvents);
        Assert.Contains(h.Log.Lines, l => l.Contains("ウィンドウ操作のキーが空"));
        h.Session.UpdateSettings(config with { WindowKeys = new() { Maximize = "Ctrl+Alt+Shift+F10", NextMaximized = "Ctrl+Alt+Shift+F11" } });
        h.Buttons(0x53); h.Session.Pump();
        Assert.Equal("Ctrl+Alt+Shift+F11", h.Input.KeyEvents[^1]);
    }
    [Fact] public void ThreeMiddlePressesToggleDpiAndReconnectResets()
    {
        var h = new Harness(); h.Activate(); h.Sent.Clear();
        for (int i = 0; i < 3; i++) { h.Buttons(0x52); h.Session.Pump(); h.Buttons(); h.Session.Pump(); }
        Assert.Equal(new[] { 1100, 2200, 1100 }, h.Writes.Select(r => (int)HidReport.U16(r.AsSpan(5, 2))));
        h.Link(); h.Session.Pump(); h.Sent.Clear(); h.Buttons(0x52); h.Session.Pump();
        Assert.Equal(1100, HidReport.U16(h.Writes[0].AsSpan(5, 2)));
    }
    [Theory] [InlineData(20, 3)] [InlineData(13, 3)] [InlineData(19, 2)]
    public void OptionsRetreatIsImmediateAndDiscardsQueuedInput(byte f, byte fn)
    {
        var h = new Harness(); h.Activate(); h.Sent.Clear();
        h.Buttons(0x56); h.Options(f, fn); Assert.Equal(SessionState.Retreated, h.Session.State);
        h.Buttons(0x53); h.Session.Pump(); Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Writes);
    }
    [Fact] public void ReadOnlyOptionsRepliesAndOtherDevicesDoNotRetreat()
    {
        var h = new Harness(); h.Activate(); h.Options(20, 2); h.Options(13, 2);
        h.Session.Receive(Harness.Frame(3, 20, 3, 14));
        Assert.Equal(SessionState.Active, h.Session.State);
    }
    [Fact] public void StopsApplyingWhenOptionsArrivesMidWrite()
    {
        var h = new Harness();
        h.Intercept = r => { if (r[2] == 13 && r[3] == 0x35) { h.Options(); return true; } return false; };
        h.Activate(); Assert.Equal(SessionState.Retreated, h.Session.State); Assert.Equal(2, h.Writes.Length);
        h.Session.Pump(); Assert.Equal(2, h.Writes.Length);
    }
    [Fact] public void EarlyOptionsReplyDuringDiscoveryPreventsAllWrites()
    {
        var h = new Harness(); h.Link(); h.Options(); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State); Assert.Empty(h.Writes);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void CleanupTouchesOnlyUnclaimedCidsOnce(bool allClaimed)
    {
        var h = new Harness(); h.Activate(); h.Options();
        foreach (ushort c in GapSession.ButtonOrder)
            if (c != 0x53 || allClaimed) h.Options(13, 3, (byte)(c >> 8), (byte)c, 0x23);
        h.Options(19, 2); h.Sent.Clear();
        h.Advance(14.999); Assert.Empty(h.Sent); h.Advance(0.001);
        if (allClaimed) Assert.Empty(h.Writes);
        else { var r = Assert.Single(h.Writes); Assert.Equal(new byte[] { 0, 0x53, 2, 0, 0x53 }, r[4..9]); }
        int count = h.Sent.Count; h.Advance(60); Assert.Equal(count, h.Sent.Count);
    }
    [Fact] public void CleanupRestoresUnclaimedThumbAndSkipsAlreadyOffCid()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Activate(); h.Options();
        foreach (var c in GapSession.ButtonOrder) h.Cids[c] = 0;
        h.Sent.Clear(); h.Advance(15);
        var r = Assert.Single(h.Writes); Assert.Equal(19, r[2]); Assert.Equal(new byte[] { 0, 0 }, r[4..6]);
    }
    [Fact] public void OptionsClaimBetweenCleanupReadAndWritePreventsOff()
    {
        var h = new Harness(); h.Activate(); h.Options();
        foreach (ushort c in GapSession.ButtonOrder.Where(c => c != 0x53)) h.Options(13, 3, (byte)(c >> 8), (byte)c, 0x23);
        h.Options(19, 2);
        h.Intercept = r =>
        {
            if (r[2] == 13 && r[3] == 0x25) { h.Reply(r); h.Options(13, 3, 0, 0x53, 0x23); return true; }
            return false;
        };
        h.Sent.Clear(); h.Advance(15); Assert.Empty(h.Writes);
    }
    [Fact] public void TimeoutRetriesThreeTimesThenWaitsForActivity()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Intercept = r => r[2] == 20 && r[3] == 0x35; h.Activate();
        for (int i = 0; i < 4; i++) h.Advance(1);
        Assert.Equal(4, h.Writes.Length); Assert.Equal(SessionState.Active, h.Session.State);
        h.Advance(100); Assert.Equal(4, h.Writes.Length);
        h.Intercept = null; h.Session.Receive(Harness.Frame(2, 4, 0, 0, 1, 1, 1)); h.Session.Pump();
        Assert.Equal(11, h.Writes.Length);
    }
    [Fact] public void BusyRetriesAndError10DoesNotRetryImmediately()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); int busy = 0;
        h.Intercept = r =>
        {
            if (r[2] != 20 || r[3] != 0x35 || busy++ != 0) return false;
            byte[] error = new byte[20]; error[0] = 0x11; error[1] = 2; error[2] = 0xFF; error[3] = 20; error[4] = 0x35; error[5] = 8;
            h.Session.Receive(error); return true;
        };
        h.Activate(); h.Advance(0.05); Assert.Equal(8, h.Writes.Length);
        h.Link(); h.AutoReply = false; h.Session.Pump();
        h.Session.Receive([0x10, 2, 0x8F, 0, 5, 9, 0]);
        int count = h.Sent.Count; h.Advance(5); Assert.Equal(count, h.Sent.Count);
    }
    [Fact] public void PauseDisconnectAndResumeDropPendingWork()
    {
        var h = new Harness(); h.Activate(); h.Sent.Clear(); h.Buttons(0x56);
        h.Session.SetPaused(true); h.Session.Pump(); h.Link(); h.Session.Pump(); Assert.Empty(h.Sent);
        h.Session.SetPaused(false); h.Session.Pump(); h.Advance(20); Assert.Empty(h.Sent); Assert.Empty(h.Input.KeyEvents);
        h.Cids[0x52] = 0; h.Link(); h.Session.Pump(); Assert.NotEmpty(h.Writes);
        h.Session.ReceiverLost(); h.Sent.Clear(); h.Buttons(0x56); h.Advance(60);
        Assert.Equal(SessionState.Disconnected, h.Session.State); Assert.Empty(h.Sent);
    }
    [Fact] public void UnknownDeviceStartupStopsWhenNameFeatureIsAbsent()
    {
        var h = new Harness(); h.Session.ProbeExisting(); h.Session.Pump();
        Assert.Equal(12, h.Sent.Count);
        Assert.All(h.Sent, r =>
        {
            Assert.Equal(0, r[2]);
            Assert.True(r[3] == 0x15 || (r[3] == 5 && r[4] == 0 && r[5] == 5));
        });
        Assert.Equal(SessionState.Disconnected, h.Session.State);
    }
    [Theory] [InlineData(true, true, SessionState.Retreated)] [InlineData(true, false, SessionState.Active)] [InlineData(false, true, SessionState.Active)]
    public void ConfirmedStartupChecksDiversionAndOptions(bool options, bool diverted, SessionState expected)
    {
        var h = new Harness(); h.Session.Receive([0x10, 2, 0x41, 0x10, 0x42, 0x42, 0xB0]);
        h.Cids[0x52] = diverted ? (byte)1 : (byte)0;
        h.OptionsRunning = options; h.Session.ProbeExisting(); h.Session.Pump(); Assert.Equal(expected, h.Session.State);
        if (expected == SessionState.Retreated) { h.Advance(20); Assert.Empty(h.Writes); }
    }
    [Fact] public void FeatureIndexesAreResolvedEverySession()
    {
        var h = new Harness(); h.Features[0x2201] = 30; h.Features[0x1B04] = 31; h.Features[0x2150] = 32;
        h.Activate(); Assert.Equal(30, h.Writes[0][2]); h.Link(); h.Session.Pump();
        Assert.Equal(8, h.Sent.Count(r => r[2] == 0 && r[3] == 5));
        Assert.All(h.Writes, r => Assert.Equal(2, r[1]));
    }
    [Fact] public void ThumbNotificationsAreIgnoredAfterRetreat()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Activate();
        for (int i = 0; i < 12; i++) { h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 1, 0, 0, 1, 2)); h.Session.Pump(); }
        Assert.Equal(120, h.Input.WheelEvents.Sum()); h.Options();
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 1, 0, 0, 1, 2)); h.Session.Pump();
        Assert.Equal(120, h.Input.WheelEvents.Sum());
    }
}
