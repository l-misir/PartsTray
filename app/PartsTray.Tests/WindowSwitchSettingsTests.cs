// ホイール選択と前へのキーの設定値・互換性・重複検査を検証します。
// windowKeys の設定や肩代わりボタンの選択肢を変えるときに開きます。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WindowSwitchSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"windowKeys\":{}}")]
    [InlineData("{\"windowKeys\":{\"nextMaximized\":\"Ctrl+F11\"}}")]
    public void OldSettingsKeepDefaults(string json)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        s.Validate();
        Assert.Equal("", s.WindowKeys.Maximize);
        Assert.Equal("", s.WindowKeys.PreviousMaximized);
        if (json == "{}") Assert.Equal("", s.WindowKeys.NextMaximized);
        Assert.Equal(1500, s.WindowKeys.WheelSwitchMs);
        Assert.Equal(120, s.WindowKeys.WheelSwitchStep);
        Assert.False(s.WindowKeys.ReverseHorizontal);
    }

    [Theory]
    [InlineData(-1, 120)] [InlineData(5001, 120)]
    [InlineData(1500, 0)] [InlineData(1500, 1201)]
    public void RejectsOutOfRangeValues(int ms, int step)
    {
        var s = new AppSettings { WindowKeys = new() { WheelSwitchMs = ms, WheelSwitchStep = step } };
        Assert.Throws<InvalidDataException>(s.Validate);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(5000, 1200)]
    public void BoundaryValuesAndNewSettingsRoundTrip(int ms, int step)
    {
        var keys = new WindowKeySettings { PreviousMaximized = "Ctrl+Alt+F12", WheelSwitchMs = ms,
            WheelSwitchStep = step, ReverseHorizontal = true };
        var s = new AppSettings { WindowKeys = keys };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, AppSettings.JsonOptions), AppSettings.JsonOptions)!;
        loaded.Validate();
        Assert.Equal(keys, loaded.WindowKeys);
    }

    [Theory]
    [InlineData("Ctrl+Alt+F10", "alt + CONTROL + f10", "")]
    [InlineData("Ctrl+F10", "", "control+F10")]
    [InlineData("", "Win+Up", "Windows+↑")]
    public void RejectsEachPairOfDuplicateKeysIncludingAliases(string maximize, string next, string previous)
    {
        var s = new WindowKeySettings { Maximize = maximize, NextMaximized = next, PreviousMaximized = previous };
        Assert.Throws<InvalidDataException>(s.Validate);
    }

    [Fact]
    public void AcceptsDistinctOrEmptyKeysButRejectsMalformedPrevious()
    {
        new WindowKeySettings().Validate();
        new WindowKeySettings { Maximize = "Ctrl+F10", NextMaximized = "Ctrl+F11", PreviousMaximized = "Ctrl+F12" }.Validate();
        var bad = new WindowKeySettings { PreviousMaximized = "F12" };
        Assert.Throws<InvalidDataException>(bad.Validate);
    }

    [Fact]
    public void PreviousButtonChoiceRoundTripsAndValidates()
    {
        var button = SettingsText.ParseButton("PreviousMaximized");
        Assert.Equal("PreviousMaximized", button!.Action);
        Assert.Equal("PreviousMaximized", SettingsText.ButtonChoice(button));
        var s = new AppSettings { GapFiller = SettingsText.SetButton(new(), "0x0053", "PreviousMaximized") };
        s.Validate();
    }
}
