// なめらかな字形を詰めるとき、ごく薄い画素も寸法に含め、元の濃淡を描けることを確認します。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DigitCoverageTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    public void UniformChoosesLargestSizeWithOneBlankColumnAndKeepsFaintEdges(int width)
    {
        static byte[,] Glyph(char digit, float pixels)
        {
            int w = (int)Math.Ceiling(pixels / (digit == '1' ? 4 : 2));
            var glyph = new byte[2, w];
            for (int x = 0; x < w; x++)
            {
                glyph[0, x] = (byte)(x == 0 ? 1 : 255);
                glyph[1, x] = (byte)(x == w - 1 ? 2 : 128);
            }
            return glyph;
        }
        // 0〜150 で幅が最大の一例は「100」。
        int WidthOf100(float px) => Glyph('1', px).GetLength(1) + 2 * Glyph('0', px).GetLength(1) + 2;
        var layout = PackedDigits<byte>.Fit(width, width, 150, Glyph);
        Assert.InRange(WidthOf100(layout.Pixels), 1, width);
        Assert.True(WidthOf100(layout.Pixels + .25f) > width);
        var mask = layout.Draw(100);
        int left = 0;
        foreach (char digit in "100")
        {
            var glyph = Glyph(digit, layout.Pixels);
            for (int y = 0; y < glyph.GetLength(0); y++)
                for (int x = 0; x < glyph.GetLength(1); x++)
                    Assert.Equal(glyph[y, x], mask[y, left + x]);
            left += glyph.GetLength(1);
            if (left == mask.GetLength(1)) break;
            Assert.Equal(0, mask[0, left]);
            Assert.Equal(0, mask[1, left]);
            left++;
        }
    }

    [Fact]
    public void NarrowUsesTheStrongestCoverageWhenColumnsOverlap()
    {
        static byte[,] Glyph(char _) => new byte[,] { { 12, 185, 97, 36 } };
        var mask = HundredDigits.TryLayout(100, 8, "narrow", Glyph, byte.MaxValue);
        Assert.NotNull(mask);
        Assert.Equal(new byte[] { 185, 97, 0, 185, 97, 0, 185, 97 }, mask.Cast<byte>());
    }

    [Fact]
    public void NarrowOneUsesTheSuppliedFullCoverageForOne()
    {
        static byte[,] Glyph(char _) => new byte[,] { { 12, 185 }, { 97, 36 } };
        var mask = HundredDigits.TryLayout(100, 7, "narrowOne", Glyph, (byte)240);
        Assert.NotNull(mask);
        Assert.Equal(new byte[] { 240, 0, 12, 185, 0, 12, 185, 240, 0, 97, 36, 0, 97, 36 }, mask.Cast<byte>());
    }
}
