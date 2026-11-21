// 右ボタン＋ホイール設定の既定値と古い JSON との互換性を検証します。
// 設定キーや保存形式を変えるときに開きます。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class RightWheelSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"gesture\":{}}")]
    [InlineData("{\"gesture\":{\"enabled\":true,\"strokeDistancePx\":20},\"windowKeys\":{\"nextMaximized\":\"Ctrl+F11\"}}")]
    public void MissingSettingKeepsFeatureOff(string json)
    {
        Assert.False(new GestureSettings().RightWheelSwitch);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        loaded.Validate(); Assert.False(loaded.Gesture.RightWheelSwitch);
    }

    [Fact]
    public void SettingUsesCamelCaseAndRoundTrips()
    {
        var settings = new AppSettings { Gesture = new() { RightWheelSwitch = true } };
        string json = JsonSerializer.Serialize(settings, AppSettings.JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("gesture").GetProperty("rightWheelSwitch").GetBoolean());
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        loaded.Validate(); Assert.True(loaded.Gesture.RightWheelSwitch);
        Assert.False(loaded.Gesture.Enabled);
    }
}
