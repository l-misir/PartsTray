// 配布時の既定値と追加設定の検査・保存往復を検証します。
// 設定キーや既定値を変えたときはこのテストを更新します。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DistributionSettingsTests
{
    [Fact]
    public void HiddenGapTabPreservesEveryOriginalValue()
    {
        var original = new AppSettings { GapFiller = new() { Enabled = false, NotifyOnSwitch = false,
            Dpi = new() { Primary = 3000, Secondary = 1500 }, Buttons = new(),
            ThumbWheel = new() { SpeedPercent = 50.123m, Mode = "off" }, CleanupDelaySeconds = 123 } };
        var edited = new AppSettings { GapFiller = new() { Enabled = true }, Monitor = new() { TaskbarTheme = "light" } };
        var result = SettingsText.MergeEditable(original, edited, editGap: false);
        Assert.Same(original.GapFiller, result.GapFiller);
        Assert.Equal("light", result.Monitor.TaskbarTheme);
    }

    [Theory]
    [InlineData("{}", false)] [InlineData("{\"gapFiller\":{}}", false)]
    [InlineData("{\"gapFiller\":{\"enabled\":true}}", true)]
    [InlineData("{\"gapFiller\":{\"enabled\":false}}", false)]
    public void DefaultsKeepExplicitGapSetting(string json, bool enabled)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        s.Validate(); Assert.Equal(enabled, s.GapFiller.Enabled);
        Assert.Empty(s.Battery.Kinds); Assert.Equal("auto", s.Monitor.TaskbarTheme);
        Assert.Equal(2200, s.GapFiller.Dpi.Primary); Assert.Equal(1100, s.GapFiller.Dpi.Secondary);
        Assert.Equal(5, s.GapFiller.Buttons.Count); Assert.Equal("hook", s.GapFiller.ThumbWheel.Mode);
    }

    // 11/20：残量 100% の虹の輪。既定は有効・1 周 1100ms。1 周の時間は 200〜10000ms だけ受け付ける。
    [Theory]
    [InlineData("{}", true, 1100)]
    [InlineData("{\"battery\":{\"rainbowWhenFull\":false,\"rainbowLapMs\":200}}", false, 200)]
    [InlineData("{\"battery\":{\"rainbowLapMs\":10000}}", true, 10000)]
    public void RainbowSettingsDefaultsAndRange(string json, bool enabled, int lap)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        s.Validate(); Assert.Equal(enabled, s.Battery.RainbowWhenFull); Assert.Equal(lap, s.Battery.RainbowLapMs);
    }

    [Theory]
    [InlineData(199)] [InlineData(10001)] [InlineData(0)]
    public void RainbowLapOutOfRangeIsRejected(int lap)
    {
        var s = JsonSerializer.Deserialize<AppSettings>($"{{\"battery\":{{\"rainbowLapMs\":{lap}}}}}", AppSettings.JsonOptions)!;
        Assert.Throws<InvalidDataException>(s.Validate);
    }

    [Theory]
    [InlineData("mouse")] [InlineData("keyboard")] [InlineData("headphones")]
    [InlineData("earbuds")] [InlineData("gamepad")] [InlineData("other")]
    public void KindRoundTrip(string kind)
    {
        var s = new AppSettings { Battery = new() { Kinds = new() { ["自分の機器"] = kind, ["未接続"] = "other" } } };
        s.Validate();
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, AppSettings.JsonOptions), AppSettings.JsonOptions)!;
        copy.Validate(); Assert.Equal(kind, copy.Battery.Kinds["自分の機器"]);
        Assert.Equal("other", copy.Battery.Kinds["未接続"]);
        Assert.Equal(kind, SettingsText.MergeEditable(s, s with { Monitor = new() { TaskbarTheme = "light" } }).Battery.Kinds["自分の機器"]);
    }

    [Theory]
    [InlineData("auto")] [InlineData("dark")] [InlineData("light")]
    public void ThemeRoundTrip(string theme)
    {
        var s = new AppSettings { Monitor = new() { TaskbarTheme = theme } }; s.Validate();
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, AppSettings.JsonOptions), AppSettings.JsonOptions)!;
        copy.Validate(); Assert.Equal(theme, copy.Monitor.TaskbarTheme);
    }

    [Theory]
    [InlineData("{\"battery\":{\"kinds\":null}}")]
    [InlineData("{\"battery\":{\"kinds\":{\"x\":null}}}")]
    [InlineData("{\"battery\":{\"kinds\":{\"x\":\"auto\"}}}")]
    [InlineData("{\"battery\":{\"kinds\":{\"x\":\"Mouse\"}}}")]
    [InlineData("{\"battery\":{\"kinds\":{\"x\":\"unknown\"}}}")]
    [InlineData("{\"battery\":{\"kinds\":{\" \":\"other\"}}}")]
    [InlineData("{\"monitor\":{\"taskbarTheme\":null}}")]
    [InlineData("{\"monitor\":{\"taskbarTheme\":\"Light\"}}")]
    [InlineData("{\"monitor\":{\"taskbarTheme\":\"system\"}}")]
    public void InvalidValuesAreRejected(string json) => Assert.Throws<InvalidDataException>(() =>
        JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!.Validate());

    [Fact]
    public void DisabledGapSendsNothingButRemembersSupportedConnection()
    {
        var h = new Harness(new AppSettings());
        Assert.False(h.Session.SupportedMouseSeen);
        h.Session.ReceiverOpened("receiver"); h.Session.ProbeExisting(); h.Session.Pump();
        h.Session.Receive([0x10, 3, 0x41, 0x10, 2, 0x78, 0xB3]);
        Assert.False(h.Session.SupportedMouseSeen);
        h.Link(); h.Buttons(0x56); h.Session.Pump(); h.Advance(1000);
        Assert.True(h.Session.SupportedMouseSeen); Assert.Equal(SessionState.Paused, h.Session.State);
        Assert.False(h.Session.TryTakeHorizontal(120));
        Assert.Empty(h.Sent); Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents);
        h.Session.ReceiverLost(); Assert.True(h.Session.SupportedMouseSeen);
    }

    [Fact]
    public void EnablingWaitsForNextConnectionAndDisablingStopsWrites()
    {
        var h = new Harness(new AppSettings()); h.Link();
        h.Session.UpdateSettings(new() { GapFiller = new() { Enabled = true } }); h.Session.Pump();
        Assert.Empty(h.Sent);
        h.Link(); h.Session.Pump(); Assert.NotEmpty(h.Writes);
        h.Sent.Clear(); h.Session.UpdateSettings(new()); h.Link(); h.Buttons(0x56); h.Advance(100);
        Assert.Empty(h.Sent); Assert.Empty(h.Input.KeyEvents); Assert.False(h.Session.TryTakeHorizontal(120));
    }
}
