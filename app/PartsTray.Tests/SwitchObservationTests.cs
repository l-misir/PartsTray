using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class SwitchObservationTests
{
    private static string[] IgnoredButtons(Harness h) => h.Log.Lines.Where(l => l.Contains("押下: PartsTray は実行しない")).ToArray();
    private static string[] IgnoredThumb(Harness h) => h.Log.Lines.Where(l => l.StartsWith("サムホイール回転を検出:")).ToArray();
    private static string[] NoticeLogs(Harness h) => h.Log.Lines.Where(l => l.StartsWith("通知: ")).ToArray();
    private static void Thumb(Harness h) => h.Session.Receive(Harness.Frame(2, h.Features[0x2150], 0, 0, 0, 1, 0, 0, 1, 2));
    private static void StartAlreadyConfigured(Harness h)
    {
        h.Cids[0x52] = 1;
        h.Session.Receive([0x10, 2, 0x41, 0x10, 0x42, 0x42, 0xB0]);
        h.Session.ProbeExisting(); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
    }

    [Fact] public void RetreatedPressesAreLoggedOncePerEdgeWithoutSending()
    {
        var h = new Harness(); h.Activate(); h.Options(); h.Sent.Clear();
        h.Buttons(0x53); h.Session.Pump();
        h.Buttons(0x53); h.Session.Pump();
        h.Buttons(); h.Session.Pump();
        h.Buttons(0x53); h.Session.Pump();
        h.Buttons(0x53); h.Session.Pump();
        Assert.Equal(new[]
        {
            "ボタン CID=0053 押下: PartsTray は実行しない（Options+ に引き継ぎ済み） 設定=Keys Ctrl+V",
            "ボタン CID=0053 押下: PartsTray は実行しない（Options+ に引き継ぎ済み） 設定=Keys Ctrl+V"
        }, IgnoredButtons(h));
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents); Assert.Empty(h.Sent);
    }

    [Fact] public void HeldActivePressIsNotReportedAsANewPressAfterHandoff()
    {
        var h = new Harness(); h.Activate(); h.Buttons(0x53); h.Session.Pump();
        Assert.Equal(new[] { "Ctrl+V" }, h.Input.KeyEvents);
        Assert.Empty(IgnoredButtons(h));
        h.Options(); h.Buttons(0x53); h.Session.Pump();
        Assert.Empty(IgnoredButtons(h));
        h.Buttons(); h.Buttons(0x53); h.Session.Pump();
        Assert.Single(IgnoredButtons(h)); Assert.Single(h.Input.KeyEvents);
    }

    [Fact] public void ConfiguredStartupRecordsIgnoredPressesWithoutSwitchNotices()
    {
        var h = new Harness(); var notices = new List<string>();
        h.Session.SwitchNotice += notices.Add;
        StartAlreadyConfigured(h); h.Sent.Clear();
        h.Buttons(0x53); h.Session.Pump();
        Assert.Equal("ボタン CID=0053 押下: PartsTray は実行しない（Options+ に引き継ぎ済み） 設定=Keys Ctrl+V", Assert.Single(IgnoredButtons(h)));
        Assert.Empty(notices); Assert.Empty(NoticeLogs(h));
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Sent);
    }

    [Fact] public void IgnoredThumbIsLoggedOncePerLinkAndNeverSent()
    {
        var h = new Harness(); h.Activate(); h.Options(); h.Sent.Clear();
        for (int i = 0; i < 20; i++) { Thumb(h); h.Session.Pump(); }
        const string expected = "サムホイール回転を検出: PartsTray は送信しない（Options+ に引き継ぎ済み）※このリンクでは 1 回だけ記録";
        Assert.Equal(expected, Assert.Single(IgnoredThumb(h)));
        Assert.Empty(h.Input.WheelEvents); Assert.Empty(h.Sent);
        h.Link(); h.Session.Pump(); h.Sent.Clear();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        for (int i = 0; i < 20; i++) { Thumb(h); h.Session.Pump(); }
        Assert.Equal(new[] { expected, expected }, IgnoredThumb(h));
        Assert.Empty(h.Input.WheelEvents); Assert.Empty(h.Sent);
    }

    [Theory]
    [InlineData(1, false)] [InlineData(-1, false)]
    [InlineData(1, true)] [InlineData(-1, true)]
    public void ZeroAndIncompleteThumbReportsDoNotConsumeFirstRotationLog(short rotation, bool startup)
    {
        var h = new Harness();
        if (startup) h.Intercept = r => r[2] == h.Features[0x1B04] && r[3] == 0x25;
        h.Activate();
        if (!startup) h.Options();
        h.Sent.Clear();
        byte[] zero = Harness.Frame(2, h.Features[0x2150], 0, 0, 0, 0, 0, 0, 1, 2);
        for (int i = 0; i < 20; i++) { h.Session.Receive(zero); h.Session.Pump(); }
        Assert.Empty(IgnoredThumb(h));
        // A short HID report has only three parameter bytes, even when its rotation bytes are nonzero.
        h.Session.Receive([0x10, 2, h.Features[0x2150], 0, 0, 1, 0]); h.Session.Pump();
        Assert.Empty(IgnoredThumb(h));
        byte[] rotated = Harness.Frame(2, h.Features[0x2150], 0, 0, (byte)(rotation >> 8), (byte)rotation, 0, 0, 1, 2);
        h.Session.Receive(rotated); h.Session.Pump();
        string reason = startup ? "接続判定中" : "Options+ に引き継ぎ済み";
        Assert.Equal($"サムホイール回転を検出: PartsTray は送信しない（{reason}）※このリンクでは 1 回だけ記録", Assert.Single(IgnoredThumb(h)));
        for (int i = 0; i < 20; i++)
        {
            h.Session.Receive(zero); h.Session.Receive(rotated); h.Session.Pump();
        }
        Assert.Single(IgnoredThumb(h));
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents); Assert.Empty(h.Sent);
    }

    [Fact] public void InvalidationRecordsEveryQueuedButtonAndAggregatesThumb()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Activate(); h.Sent.Clear();
        h.Buttons(0x53, 0x52); h.Buttons(); h.Buttons(0x53);
        for (int i = 0; i < 3; i++) Thumb(h);
        h.Options(); h.Session.Pump();
        Assert.Equal(new[]
        {
            "未実行のボタン操作を破棄 CID=0053 設定=Keys Ctrl+V",
            "未実行のボタン操作を破棄 CID=0052 設定=ToggleDpi ",
            "未実行のボタン操作を破棄 CID=0053 設定=Keys Ctrl+V"
        }, h.Log.Lines.Where(l => l.StartsWith("未実行のボタン操作を破棄")));
        Assert.Equal("未送信のサムホイール 3 件を破棄", Assert.Single(h.Log.Lines, l => l.StartsWith("未送信のサムホイール")));
        int discarded = h.Log.Lines.IndexOf("未送信のサムホイール 3 件を破棄");
        Assert.StartsWith("状態 Active -> Retreated:", h.Log.Lines[discarded + 1]);
        h.Buttons(0x53); h.Session.Pump();
        Assert.Empty(IgnoredButtons(h));
        h.Options(); h.Session.ReceiverLost();
        Assert.Equal(3, h.Log.Lines.Count(l => l.StartsWith("未実行のボタン操作を破棄")));
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents); Assert.Empty(h.Sent);
    }

    [Fact] public void ExecutedActionsAndEmptyQueuesHaveNoDiscardLogs()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); h.Activate(); h.Buttons(0x53); Thumb(h); h.Session.Pump();
        h.Options(); h.Session.Pump();
        Assert.Single(h.Input.KeyEvents); Assert.Single(h.Input.WheelEvents);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("破棄"));
    }

    [Fact] public void PausedInputIsNotObservedOrSent()
    {
        var h = new Harness(); h.Activate(); h.Session.SetPaused(true);
        h.Log.Lines.Clear(); h.Sent.Clear();
        h.Buttons(0x53); h.Buttons(); h.Buttons(0x53); Thumb(h); h.Session.Pump();
        Assert.Empty(h.Log.Lines); Assert.Empty(h.Sent);
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents);
        h.Session.SetPaused(false); h.Buttons(0x53); h.Session.Pump();
        Assert.Single(IgnoredButtons(h));
    }

    [Fact] public void StartupObservationUsesCheckReasonAndSurvivesHandoff()
    {
        var h = new Harness(); var notices = new List<string>();
        h.Session.SwitchNotice += notices.Add;
        h.Intercept = r => r[2] == h.Features[0x1B04] && r[3] == 0x25;
        h.Activate(); // Feature discovery completed, but the startup getCidReporting reply is held.
        h.Buttons(0x53); Thumb(h); h.Buttons(0x53); Thumb(h);
        Assert.Equal("ボタン CID=0053 押下: PartsTray は実行しない（接続判定中） 設定=Keys Ctrl+V", Assert.Single(IgnoredButtons(h)));
        Assert.Contains("（接続判定中）", Assert.Single(IgnoredThumb(h)));
        h.Options(); h.Buttons(0x53); Thumb(h); h.Session.Pump();
        Assert.Single(IgnoredButtons(h)); Assert.Single(IgnoredThumb(h));
        h.Buttons(); h.Buttons(0x53);
        Assert.Contains("（Options+ に引き継ぎ済み）", IgnoredButtons(h)[1]);
        Assert.Empty(notices); Assert.Empty(NoticeLogs(h));
        Assert.Empty(h.Writes); Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents);
    }

    [Fact] public void ObservationUsesSessionBindingsAndIgnoresUnconfiguredButtons()
    {
        var config = new AppSettings { GapFiller = new() { Enabled = true, Buttons = new()
        {
            ["0x0053"] = new() { Keys = "PrintScreen" },
            ["0x0052"] = new() { Action = "ToggleDpi" }
        } } };
        var h = new Harness(config); h.Activate(); h.Options();
        h.Session.UpdateSettings(new() { GapFiller = new() { Enabled = true } });
        h.Buttons(0x53, 0x53, 0x56, 0x52); h.Session.Pump();
        Assert.Equal(new[]
        {
            "ボタン CID=0053 押下: PartsTray は実行しない（Options+ に引き継ぎ済み） 設定=Keys PrintScreen",
            "ボタン CID=0052 押下: PartsTray は実行しない（Options+ に引き継ぎ済み） 設定=ToggleDpi "
        }, IgnoredButtons(h));
        Assert.Empty(h.Input.KeyEvents);
    }

    [Fact] public void ObservationFiltersDeviceKindFunctionAndIncompleteButtonReports()
    {
        var h = new Harness(); h.Features[0x1B04] = 31; h.Features[0x2150] = 32;
        h.Activate(); h.Options();
        foreach (byte feature in new[] { h.Features[0x1B04], h.Features[0x2150] })
        {
            h.Session.Receive(Harness.Frame(3, feature, 0, 0, 0, 0x53));
            h.Session.Receive(Harness.Frame(2, feature, 1, 0, 0, 0x53));
            h.Session.Receive(Harness.Frame(2, feature, 0, 5, 0, 0x53));
        }
        h.Session.Receive(Harness.Frame(2, 13, 0, 0, 0, 0x53));
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 1));
        h.Session.Receive([0x10, 2, 31, 0, 0, 0x53, 0]);
        Assert.Empty(IgnoredButtons(h)); Assert.Empty(IgnoredThumb(h));
        h.Buttons(0x53); Thumb(h);
        h.Session.Receive([0x10, 2, 31, 0, 0, 0, 0]);
        h.Buttons(0x53); h.Session.Pump();
        Assert.Single(IgnoredButtons(h)); Assert.Single(IgnoredThumb(h));
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents);
    }

    [Theory] [InlineData("link")] [InlineData("receiver")] [InlineData("pause")] [InlineData("begin")]
    public void EndingOrBeginningALinkClearsHeldButtons(string transition)
    {
        var h = new Harness(); h.Activate(); h.Options(); h.Buttons(0x53);
        switch (transition)
        {
            case "link": h.Session.Receive([0x10, 2, 0x41, 0x10, 0x42, 0x42, 0xB0]); break;
            case "receiver": h.Session.ReceiverLost(); break;
            case "pause": h.Session.SetPaused(true); h.Session.SetPaused(false); break;
        }
        if (transition != "pause") { h.Link(); h.Session.Pump(); }
        h.Buttons(0x53); h.Session.Pump();
        Assert.Equal(2, IgnoredButtons(h).Length); Assert.Empty(h.Input.KeyEvents);
    }

    [Fact] public void SwitchNoticesIncludeElapsedTimesAndMatchingLogsOnlyOnce()
    {
        var h = new Harness(); var notices = new List<string>();
        h.Session.SwitchNotice += notices.Add;
        h.Clock.Now = TimeSpan.FromSeconds(10); h.Link();
        h.Clock.Now += TimeSpan.FromSeconds(0.18); h.Session.Pump();
        Assert.Equal("肩代わりを開始しました（接続から 0.18 秒）", Assert.Single(notices));
        h.Advance(13.42); h.Options(); h.Options(); h.Session.Pump();
        Assert.Equal(new[]
        {
            "肩代わりを開始しました（接続から 0.18 秒）",
            "Options+ に引き継ぎました（肩代わり 13.42 秒）"
        }, notices);
        Assert.Equal(notices.Select(n => $"通知: {n}"), NoticeLogs(h));
    }

    [Fact] public void EarlyOptionsDuringDiscoveryDoesNotNotify()
    {
        var h = new Harness(); var notices = new List<string>(); h.Session.SwitchNotice += notices.Add;
        h.Link(); h.Options(); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Empty(notices); Assert.Empty(NoticeLogs(h)); Assert.Empty(h.Writes);
    }

    [Fact] public void DisabledSwitchNoticesSuppressBothEventsAndNoticeLogs()
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, NotifyOnSwitch = false } });
        var notices = new List<string>(); h.Session.SwitchNotice += notices.Add;
        h.Activate(); Assert.Equal(SessionState.Active, h.Session.State);
        h.Options(); Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Empty(notices); Assert.Empty(NoticeLogs(h));
    }

    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public void UpdatedNotificationSettingAppliesToCurrentSession(bool initial, bool updated)
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, NotifyOnSwitch = initial } });
        var notices = new List<string>(); h.Session.SwitchNotice += notices.Add;
        h.Activate();
        Assert.Equal(initial ? 1 : 0, notices.Count);
        h.Session.UpdateSettings(new() { GapFiller = new() { Enabled = true, NotifyOnSwitch = updated } });
        h.Options();
        string notice = Assert.Single(notices);
        Assert.StartsWith(updated ? "Options+ に引き継ぎました（肩代わり " : "肩代わりを開始しました（接続から ", notice);
        Assert.Equal($"通知: {notice}", Assert.Single(NoticeLogs(h)));
    }

    [Theory] [InlineData("link")] [InlineData("receiver")] [InlineData("pause")]
    public void OtherStateTransitionsDoNotEmitHandoffNotices(string transition)
    {
        var h = new Harness(); var notices = new List<string>(); h.Session.SwitchNotice += notices.Add;
        h.Activate(); notices.Clear(); h.Log.Lines.Clear();
        switch (transition)
        {
            case "link": h.Session.Receive([0x10, 2, 0x41, 0x10, 0x42, 0x42, 0xB0]); break;
            case "receiver": h.Session.ReceiverLost(); break;
            case "pause": h.Session.SetPaused(true); h.Session.SetPaused(false); break;
        }
        h.Options(); h.Session.Pump();
        Assert.Empty(notices); Assert.Empty(NoticeLogs(h));
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"gapFiller\":{}}", true)]
    [InlineData("{\"gapFiller\":{\"notifyOnSwitch\":false}}", false)]
    [InlineData("{\"gapFiller\":{\"notifyOnSwitch\":true}}", true)]
    public void JsonNotificationSettingDefaultsToTrueAndHonorsExplicitValues(string json, bool expected)
    {
        var config = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        config.Validate(); Assert.Equal(expected, config.GapFiller.NotifyOnSwitch);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(config, AppSettings.JsonOptions));
        Assert.Equal(expected, saved.RootElement.GetProperty("gapFiller").GetProperty("notifyOnSwitch").GetBoolean());
    }
}
