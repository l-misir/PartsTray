// holdButton の既定値・JSON 往復・編集保存と不正値を検証します。
// ボタン選択肢や設定の受け付け方を変えるときに開きます。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class HoldButtonSettingsTests
{
    [Theory]
    [InlineData("{}")] [InlineData("{\"windowKeys\":{}}")]
    [InlineData("{\"windowKeys\":{\"nextMaximized\":\"Ctrl+F11\"}}")]
    public void OldSettingsKeepHoldDisabled(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal("", settings.WindowKeys.HoldButton);
    }

    [Theory]
    [InlineData("")] [InlineData("0x0052")] [InlineData("0x00C4")] [InlineData("0x00C3")]
    [InlineData("0x01A0")] [InlineData("0x0056")] [InlineData("0x0053")]
    [InlineData("0X00c4")]
    public void SupportedValuesLoadAndRoundTrip(string value)
    {
        var settings = new AppSettings { WindowKeys = new() { HoldButton = value } };
        settings.Validate();
        string json = JsonSerializer.Serialize(settings, AppSettings.JsonOptions);
        Assert.Contains("\"holdButton\"", json);
        var copy = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        copy.Validate();
        Assert.Equal(value, copy.WindowKeys.HoldButton);
        Assert.Contains(WindowHoldButton.Choices, c => c.Value == WindowHoldButton.Canonical(value));
    }

    [Theory]
    [InlineData(null)] [InlineData(" ")] [InlineData("0x0000")] [InlineData("0x00C5")]
    [InlineData("0xFFFF")] [InlineData("0xC4")] [InlineData("00C4")]
    [InlineData("0x00GG")] [InlineData("0x100C4")] [InlineData("0x00C4 ")]
    public void RejectsInvalidValues(string? value)
    {
        var settings = new AppSettings { WindowKeys = new() { HoldButton = value! } };
        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Fact]
    public void EditMergePreservesHoldChoiceAndOtherSettings()
    {
        var original = new AppSettings { WindowKeys = new() { Maximize = "Ctrl+F10" } };
        var edited = original with { WindowKeys = original.WindowKeys with { HoldButton = "0x00C4" } };
        var saved = SettingsText.MergeEditable(original, edited);
        Assert.Equal("0x00C4", saved.WindowKeys.HoldButton);
        Assert.Equal("Ctrl+F10", saved.WindowKeys.Maximize);
        Assert.Equal(original.GapFiller, saved.GapFiller);
    }
}
