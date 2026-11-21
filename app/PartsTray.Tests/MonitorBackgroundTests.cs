// 背景色の既定値・入力検査・旧設定との互換性・配色を検証します。
// 数字の背景設定や見やすさの補正を変えるときに開きます。
using System.Drawing;
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class MonitorBackgroundTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"monitor\":{\"cpu\":{\"color\":\"#123456\"},\"tempMemory\":{\"split\":false,\"source\":\"coretemp\"}}}")]
    public void OldJsonDefaultsToTransparent(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal("", settings.Monitor.Cpu.BackgroundColor);
        Assert.Equal("", settings.Monitor.TempMemory.TempBackgroundColor);
        Assert.Equal("", settings.Monitor.TempMemory.MemoryBackgroundColor);
        if (json != "{}")
        {
            Assert.Equal("#123456", settings.Monitor.Cpu.Color);
            Assert.False(settings.Monitor.TempMemory.Split);
            Assert.Equal("coretemp", settings.Monitor.TempMemory.Source);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#aBc123")]
    public void ValidBackgroundsRoundTripIndependently(string color)
    {
        var settings = new AppSettings { Monitor = new()
        {
            Cpu = new() { BackgroundColor = color },
            TempMemory = new() { TempBackgroundColor = "#123456", MemoryBackgroundColor = "#FEDCBA" }
        } };
        settings.Validate();
        var restored = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, AppSettings.JsonOptions), AppSettings.JsonOptions)!;
        restored.Validate();
        Assert.Equal(settings.Monitor, restored.Monitor);
        Assert.Equal(color.ToUpperInvariant(), SettingsText.BackgroundColorText(color));
        Assert.Equal(restored.Monitor, SettingsText.MergeEditable(new(), restored).Monitor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#FFFFFFFF")]
    [InlineData("112233")]
    [InlineData("#12XX00")]
    [InlineData("# 12345")]
    [InlineData("#12345 ")]
    public void InvalidBackgroundsAreRejectedInEveryField(string? color)
    {
        Assert.Throws<InvalidDataException>(() => SettingsText.BackgroundColorText(color));
        Assert.Throws<InvalidDataException>(() => new MonitorSettings { Cpu = new() { BackgroundColor = color! } }.Validate());
        Assert.Throws<InvalidDataException>(() => new MonitorSettings { TempMemory = new() { TempBackgroundColor = color! } }.Validate());
        Assert.Throws<InvalidDataException>(() => new MonitorSettings { TempMemory = new() { MemoryBackgroundColor = color! } }.Validate());
    }

    [Theory]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData("#000000", "#000000")]
    [InlineData("#64D2FF", "#00FFFF")]
    [InlineData("#FF3B30", "#A00000")]
    [InlineData("#808080", "#808080")]
    public void CustomBackgroundKeepsTextReadable(string text, string background)
    {
        Color fg = ColorTranslator.FromHtml(text), bg = ColorTranslator.FromHtml(background);
        var adjusted = Palette.ForBackground(fg, bg);
        Assert.True(Palette.Contrast(adjusted, bg) >= 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarPaletteStillUsesTheSameRules(bool light)
    {
        foreach (var color in new[] { Palette.White, Palette.Red, Palette.Cyan, Palette.Green })
            Assert.Equal(Palette.ForTaskbar(color, light), Palette.ForBackground(color, Palette.Background(light)));
    }
}
