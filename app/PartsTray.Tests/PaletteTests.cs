// WCAG のコントラストと、暗いテーマの既存色の維持を検証します。
// 色補正の計算や基準背景を変えたときに実行する純粋なテストです。
using System.Drawing;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class PaletteTests
{
    [Theory]
    [InlineData(0xFFFFFF)] [InlineData(0x64D2FF)] [InlineData(0xFF3B30)]
    [InlineData(0x43FF72)] [InlineData(0xFF9500)] [InlineData(0xFFD60A)]
    [InlineData(0x34C759)] [InlineData(0x00FF00)] [InlineData(0xFF0000)]
    public void ExistingDarkColorsAreUnchanged(int rgb)
    {
        var c = Color.FromArgb(255, Color.FromArgb(rgb));
        Assert.True(Palette.Contrast(c, Palette.DarkBackground) >= 3);
        Assert.Equal(c.ToArgb(), Palette.ForTaskbar(c, false).ToArgb());
    }

    [Fact]
    public void BrighteningIsExactlyTheLegacyCalculation()
    {
        Assert.Equal(Color.FromArgb(67, 255, 114).ToArgb(), Palette.Brighten(Palette.Green).ToArgb());
        Assert.Equal(Palette.Red, Palette.Brighten(Palette.Red));
        Assert.Equal(Palette.Orange, Palette.Brighten(Palette.Orange));
    }

    [Fact]
    public void LightThemeDarkensWhiteAndBrightColors()
    {
        foreach (var c in new[] { Palette.White, Palette.Cyan, Palette.Green, Palette.Bolt })
        {
            var next = Palette.ForTaskbar(c, true);
            Assert.True(next.GetBrightness() < c.GetBrightness());
            Assert.True(Palette.Contrast(next, Palette.LightBackground) >= 3);
        }
        var white = Palette.ForTaskbar(Palette.White, true);
        Assert.Equal(white.R, white.G); Assert.Equal(white.R, white.B); Assert.True(white.R < 150);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ColorCubeAndCustomColorsMeetContrastWithoutChangingAlreadyReadableColors(bool light)
    {
        for (int r = 0; r <= 255; r += 17)
            for (int g = 0; g <= 255; g += 17)
                for (int b = 0; b <= 255; b += 17)
                {
                    var c = Color.FromArgb(123, r, g, b);
                    var next = Palette.ForTaskbar(c, light);
                    Assert.Equal(c.A, next.A);
                    Assert.True(Palette.Contrast(next, Palette.Background(light)) >= 3, $"{c} -> {next}");
                    if (Palette.Contrast(c, Palette.Background(light)) >= 3) Assert.Equal(c, next);
                }
    }

    // 11/20：虹の色。0 度が赤、120 度が緑、240 度が青。360 度ごとに同じ色に戻る（マイナスも）。
    [Fact]
    public void RainbowHuesWrapAndHitPrimaryColors()
    {
        Assert.Equal(Color.FromArgb(255, 38, 38).ToArgb(), Palette.Rainbow(0).ToArgb());
        Assert.Equal(Color.FromArgb(38, 255, 38).ToArgb(), Palette.Rainbow(120).ToArgb());
        Assert.Equal(Color.FromArgb(38, 38, 255).ToArgb(), Palette.Rainbow(240).ToArgb());
        Assert.Equal(Palette.Rainbow(30).ToArgb(), Palette.Rainbow(390).ToArgb());
        Assert.Equal(Palette.Rainbow(330).ToArgb(), Palette.Rainbow(-30).ToArgb());
    }

    // 虹のどの色も、補正後は明暗どちらの背景とも 3:1 以上の差がある。
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RainbowMeetsContrastAfterCorrection(bool light)
    {
        for (int hue = 0; hue < 360; hue += 4)
            Assert.True(Palette.Contrast(Palette.ForTaskbar(Palette.Rainbow(hue), light), Palette.Background(light)) >= 3, $"hue {hue}");
    }

    [Fact]
    public void WcagReferenceRatios()
    {
        Assert.Equal(21, Palette.Contrast(Color.Black, Color.White), 10);
        Assert.Equal(1, Palette.Contrast(Palette.Cyan, Palette.Cyan), 10);
        Assert.Equal(Palette.Contrast(Palette.Cyan, Palette.Red), Palette.Contrast(Palette.Red, Palette.Cyan));
    }
}
