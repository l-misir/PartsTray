// ウィンドウ操作のキーの文字（HotkeyText.cs）と、windowKeys の設定の検査を検証します。
// 使えるキーの名前や設定の決まりを変えたときに実行する純粋なテストです。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class HotkeyTextTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+F10", HotkeyText.Control | HotkeyText.Alt | HotkeyText.Shift, 0x79u)]
    [InlineData("win+ctrl+up", HotkeyText.Win | HotkeyText.Control, 0x26u)]
    [InlineData(" Alt + M ", HotkeyText.Alt, (uint)'M')]
    [InlineData("Ctrl+Alt+9", HotkeyText.Control | HotkeyText.Alt, (uint)'9')]
    [InlineData("Ctrl+Shift+PageDown", HotkeyText.Control | HotkeyText.Shift, 0x22u)]
    [InlineData("Ctrl+Alt+F24", HotkeyText.Control | HotkeyText.Alt, 0x87u)]
    public void ParsesCombinations(string text, uint modifiers, uint key)
    {
        Assert.True(HotkeyText.TryParse(text, out uint m, out uint k));
        Assert.Equal(modifiers, m); Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("")] [InlineData(null)] [InlineData("   ")]
    public void EmptyMeansNotUsed(string? text)
    {
        Assert.True(HotkeyText.TryParse(text, out uint m, out uint k));
        Assert.Equal(0u, m); Assert.Equal(0u, k);
    }

    [Theory]
    [InlineData("F10")]            // 修飾キーがない
    [InlineData("Ctrl+Alt")]       // キーがない
    [InlineData("Ctrl+A+B")]       // キーが 2 つ
    [InlineData("Ctrl+F25")]       // ない F キー
    [InlineData("Ctrl+Escape")]    // 使えない名前
    public void RejectsBadCombinations(string text) => Assert.False(HotkeyText.TryParse(text, out _, out _));

    [Fact]
    public void SettingsDefaultToNoKeysAndRejectBadOrDuplicateKeys()
    {
        var s = JsonSerializer.Deserialize<AppSettings>("{}", AppSettings.JsonOptions)!;
        s.Validate(); Assert.Equal("", s.WindowKeys.Maximize); Assert.Equal("", s.WindowKeys.NextMaximized);
        var ok = JsonSerializer.Deserialize<AppSettings>("{\"windowKeys\":{\"maximize\":\"Ctrl+Alt+Shift+F10\",\"nextMaximized\":\"Ctrl+Alt+Shift+F11\"}}", AppSettings.JsonOptions)!;
        ok.Validate();
        var bad = JsonSerializer.Deserialize<AppSettings>("{\"windowKeys\":{\"maximize\":\"F10\"}}", AppSettings.JsonOptions)!;
        Assert.Throws<InvalidDataException>(bad.Validate);
        var same = JsonSerializer.Deserialize<AppSettings>("{\"windowKeys\":{\"maximize\":\"Ctrl+Alt+F10\",\"nextMaximized\":\"ctrl + alt + f10\"}}", AppSettings.JsonOptions)!;
        Assert.Throws<InvalidDataException>(same.Validate);
    }
}
