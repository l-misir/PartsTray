// 細字 uniform の字間、全表示値の収まりと、範囲ごとの最大の共通サイズを検証します。
using System.Globalization;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class PackedDigitsTests
{
    public static TheoryData<int, int, int> Frames
    {
        get
        {
            var cases = new TheoryData<int, int, int>();
            foreach (int size in new[] { 16, 20, 24, 32 })
                foreach (int height in new[] { size, size / 2 })
                    foreach (int maximum in new[] { 100, 150 })
                        cases.Add(size, height, maximum);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void EveryValueFitsWithUnchangedGlyphsAndOneEmptyColumn(int width, int height, int maximum)
    {
        var layout = PackedDigits.Fit(width, height, maximum, PaddedDigit);
        var glyphs = "0123456789".ToDictionary(digit => digit, digit => PaddedDigit(digit, layout.Pixels));
        int inkHeight = InkHeight(layout.Pixels);

        // 温度の範囲には 100〜109 もあるので、10〜99 と合わせて全100通りの字間を調べられる。
        for (int value = 0; value <= maximum; value++)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            bool[,] mask = layout.Draw(value);
            Assert.Equal(inkHeight, mask.GetLength(0));
            Assert.InRange(mask.GetLength(0), 1, height);
            Assert.Equal(NumberWidth(text, layout.Pixels), mask.GetLength(1));
            Assert.InRange(mask.GetLength(1), 1, width);

            int left = 0;
            foreach (char digit in text)
            {
                if (left > 0)
                {
                    for (int y = 0; y < inkHeight; y++) Assert.False(mask[y, left]);
                    left++;
                }
                // 1桁で描いた元のインクを上下の位置も含めて照合し、桁ごとの縮小や切り落としを検出する。
                bool[,] source = glyphs[digit];
                int inkWidth = InkWidth(digit, layout.Pixels);
                for (int y = 0; y < inkHeight; y++)
                    for (int x = 0; x < inkWidth; x++)
                        Assert.Equal(source[1 + y, LeftPadding(digit) + x], mask[y, left + x]);
                left += inkWidth;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void ChoosesLargestSizeThatFitsTheWidestNumberAndTallestGlyph(int width, int height, int maximum)
    {
        var layout = PackedDigits.Fit(width, height, maximum, PaddedDigit);
        string[] numbers = Enumerable.Range(0, maximum + 1)
            .Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();

        bool Fits(float pixels) => InkHeight(pixels) <= height && numbers.Max(text => NumberWidth(text, pixels)) <= width;
        Assert.True(Fits(layout.Pixels));
        Assert.False(Fits(layout.Pixels + .25f));
    }

    [Fact]
    public void NativeFitUsesOnlyOfferedSizesAndKeepsTheLargestThatFits()
    {
        int[] nativeSizes = [20, 14, 10, 8];
        var tried = new List<float>();
        var layout = PackedDigits.FitNative(20, 20, 150, nativeSizes, (digit, pixels) =>
        {
            if (digit == '0') tried.Add(pixels);
            return PaddedDigit(digit, pixels);
        });

        Assert.NotNull(layout);
        Assert.Equal(10f, layout.Pixels);
        Assert.Equal(new float[] { 20, 14, 10 }, tried);
        Assert.InRange(layout.Draw(144).GetLength(1), 1, 20);
    }

    [Fact]
    public void NativeFitReturnsNullWhenEveryOfferedSizeExceedsTheFrame() =>
        Assert.Null(PackedDigits.FitNative(20, 20, 150, [20, 14, 12], PaddedDigit));

    // 「1」は細く、「4」は広い。0〜100 では「44」、0〜150 では「144」が幅を取りやすい。
    private static int InkWidth(char digit, float pixels) => (int)Math.Ceiling(pixels * (digit switch
    {
        '1' => .25f,
        '4' => .65f,
        _ => .40f,
    }));

    private static int InkHeight(float pixels) => Math.Max(3, (int)Math.Ceiling(pixels * .875f));

    private static int NumberWidth(string text, float pixels) => text.Sum(digit => InkWidth(digit, pixels)) + text.Length - 1;

    private static int LeftPadding(char digit) => 1 + (digit - '0') % 3;

    // 各桁の上下左右に異なる余白を含める。「7」だけが共通の帯のいちばん上に届く。
    private static bool[,] PaddedDigit(char digit, float pixels)
    {
        int width = InkWidth(digit, pixels), height = InkHeight(pixels);
        int left = LeftPadding(digit), top = digit == '7' ? 1 : 2;
        int bottom = digit == '1' ? height - 1 : height;
        var glyph = new bool[height + 3, left + width + 2];
        for (int x = left; x < left + width; x++) glyph[top, x] = true;
        for (int y = top; y <= bottom; y++) glyph[y, left + width - 1] = true;
        return glyph;
    }
}
