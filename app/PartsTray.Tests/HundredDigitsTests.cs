// モニターの数字が枠に収まり、各案で1〜3桁の高さと字形が保たれることを検証します。
using System.Globalization;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class HundredDigitsTests
{
    public static TheoryData<int, string> SizesAndStyles
    {
        get
        {
            var cases = new TheoryData<int, string>();
            foreach (int size in new[] { 16, 20, 24, 32 })
                foreach (string style in new[] { "shrink", "narrow", "narrowOne" })
                    cases.Add(size, style);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(SizesAndStyles))]
    public void WideDigitsFitSingleStackedAndParrotRows(int size, string style)
    {
        int[] heights = [MonitorFont.SelectHeight(size, size), MonitorFont.TempMemoryHeight(size), MonitorFont.ParrotHeight(size)];
        foreach (int height in heights.Distinct())
        {
            AssertMask(MonitorFont.GlyphsWide("--", height, size), HundredDigits.Wide(null, size, height, style));
            foreach (int value in new[] { 0, 8, 37, 99, 100, 101, 111, 149, 150 })
            {
                var mask = HundredDigits.Wide(value, size, height, style);
                Assert.Equal(height, mask.GetLength(0));
                Assert.InRange(mask.GetLength(1), 1, size);
                Assert.Contains(true, Enumerable.Range(0, mask.GetLength(1)).Select(x => mask[0, x]));
                Assert.Contains(true, Enumerable.Range(0, mask.GetLength(1)).Select(x => mask[height - 1, x]));
                if (value < 100 || style == "shrink")
                {
                    string text = MonitorFont.FitNumber(value, size, height);
                    AssertMask(MonitorFont.GlyphsWide(text, height, size), mask);
                }
            }

            if (style != "shrink")
            {
                // 幅に収まった理由が、3 桁を一律に「99」や「100」へ置き換えたためでないこと。
                var hundred = HundredDigits.Wide(100, size, height, style);
                foreach (int value in new[] { 99, 101, 110, 149 })
                {
                    var other = HundredDigits.Wide(value, size, height, style);
                    Assert.False(hundred.Cast<bool>().SequenceEqual(other.Cast<bool>()),
                        $"{size}px・高さ {height} の {style} で 100 と {value} が同じ形になりました。");
                }
            }
        }
    }

    [Theory]
    [InlineData(16, 100)]
    [InlineData(16, 150)]
    [InlineData(16, 999)]
    [InlineData(20, 100)]
    public void ShrinkPreviewDrawsAllThreeDigits(int width, int number)
    {
        var mask = HundredDigits.Wide(number, width, 9, "shrink", digitPreview: true);
        int fittedHeight = width == 16 ? 7 : 9;
        AssertMask(MonitorFont.GlyphsWide(number.ToString(CultureInfo.InvariantCulture), fittedHeight, width), mask);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void UniformDrawsEveryValueWithTheSameDotGlyphsAsThreeDigits(int size)
    {
        int[] heights = [MonitorFont.SelectHeight(size, size), MonitorFont.TempMemoryHeight(size), MonitorFont.ParrotHeight(size)];
        foreach (int height in heights.Distinct())
        {
            int expectedHeight = new[] { 9, 7, 5 }.First(h => h <= height && MonitorFont.Glyphs("100", h).GetLength(1) <= size);
            for (int value = 0; value <= 999; value++)
            {
                // 右に 0 を補って従来の点の絵で3桁描き、実際の数字にあたる部分を1ドットずつ比べる。
                string text = value.ToString(CultureInfo.InvariantCulture);
                var reference = MonitorFont.GlyphsWide(text.PadRight(3, '0'), expectedHeight, size);
                int advance = (reference.GetLength(1) + 1) / 3;
                var mask = HundredDigits.Wide(value, size, height, "uniform");
                Assert.Equal(expectedHeight, mask.GetLength(0));
                Assert.Equal(text.Length * advance - 1, mask.GetLength(1));
                Assert.InRange(mask.GetLength(1), 1, size);
                Assert.True(MatchesAt(reference, mask, 0), $"{size}px・高さ {height} の {value} の字形が3桁での字形と異なります。");
            }
        }
    }

    [Fact]
    public void NarrowOnePreservesBothZerosAndReplacesOneWithAFullHeightStroke()
    {
        // 高さ 7 の基本字形は幅 4。「0」は後ろの 2 文字とも 1 ドットも変えずに残る。
        static bool[,] Glyph(char ch) => MonitorFont.Glyphs(ch.ToString(), 7);
        var zero = Glyph('0');
        var mask = HundredDigits.TryLayout(100, 16, "narrowOne", Glyph)!;
        var narrow = HundredDigits.TryLayout(100, 16, "narrow", Glyph)!;
        Assert.False(mask.Cast<bool>().SequenceEqual(narrow.Cast<bool>()));
        Assert.Equal(7, mask.GetLength(0));
        int[] starts = Enumerable.Range(0, mask.GetLength(1) - zero.GetLength(1) + 1)
            .Where(left => MatchesAt(mask, zero, left)).ToArray();
        Assert.Equal(2, starts.Length);
        Assert.True(starts[0] > 0);
        for (int y = 0; y < mask.GetLength(0); y++)
        {
            Assert.True(mask[y, 0]);
            Assert.DoesNotContain(true, Enumerable.Range(1, starts[0] - 1).Select(x => mask[y, x]));
        }
    }

    [Fact]
    public void NarrowOneFallsBackToNarrowWhenEvenZeroSpacingDoesNotFit()
    {
        // 幅 5 の「0」を 2 つそのまま描くと、幅 10 の枠には「1」の縦線を置く余地がない。
        static bool[,] Glyph(char ch) => MonitorFont.Glyphs(ch.ToString(), 9);
        var narrow = HundredDigits.TryLayout(100, 10, "narrow", Glyph)!;
        var fallback = HundredDigits.TryLayout(100, 10, "narrowOne", Glyph)!;
        Assert.Equal(9, narrow.GetLength(0));
        Assert.InRange(narrow.GetLength(1), 1, 10);
        AssertMask(narrow, fallback);
    }

    [Theory]
    [InlineData("narrow")]
    [InlineData("narrowOne")]
    public void UnequalGlyphWidthsStillFitTheSmallestLayout(string style)
    {
        // 1 列幅の「1」が 2 個あっても、残りの「0」と字間を含めて 5 列に収める。
        bool[,] stroke = { { true }, { true }, { true }, { true }, { true }, { true }, { true } };
        var mask = HundredDigits.TryLayout(101, 5, style, ch => ch == '1' ? stroke : MonitorFont.Glyphs("0", 7))!;
        Assert.Equal(7, mask.GetLength(0));
        Assert.InRange(mask.GetLength(1), 1, 5);
    }

    private static bool MatchesAt(bool[,] mask, bool[,] glyph, int left)
    {
        for (int y = 0; y < glyph.GetLength(0); y++)
            for (int x = 0; x < glyph.GetLength(1); x++)
                if (mask[y, left + x] != glyph[y, x]) return false;
        return true;
    }

    private static void AssertMask(bool[,] expected, bool[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        Assert.Equal(expected.Cast<bool>(), actual.Cast<bool>());
    }
}
