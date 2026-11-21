// 通知領域の枠に合わせた固定の点の絵の倍率、各桁の画素と字間を照合します。
using System.Globalization;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DotDigitLayoutTests
{
    private const string Characters = "0123456789-";
    private static readonly string[] Numbers = Enumerable.Range(0, 151)
        .Select(n => n.ToString(CultureInfo.InvariantCulture)).Append("--").ToArray();

    private static string[][][] Sources(string name) => name switch
    {
        "CoreTemp" => [DotDigitSets.CoreTemp96, DotDigitSets.CoreTemp120],
        "Claude" => [DotDigitSets.Claude, DotDigitSets.ClaudeSmall],
        "File" => [DotDigitSheet.Read(43, 4, (x, y) =>
            x % 4 == 3 || (x % 4 + y + x / 4) % 3 != 0 ? (byte)0 : byte.MaxValue)],
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [InlineData("CoreTemp")]
    [InlineData("Claude")]
    public void EachSourceContainsElevenDistinctBinaryGlyphsAtOneBaseline(string name)
    {
        foreach (var source in Sources(name))
        {
            Assert.Equal(Characters.Length, source.Length);
            Assert.Equal(Characters.Length, source.Select(g => string.Join('\n', g)).Distinct().Count());
            Assert.All(source, glyph =>
            {
                Assert.NotEmpty(glyph);
                Assert.Equal(source[0].Length, glyph.Length);
                Assert.Contains(glyph, row => row.Contains('#'));
                Assert.All(glyph, row =>
                {
                    Assert.Equal(glyph[0].Length, row.Length);
                    Assert.All(row, c => Assert.True(c is '#' or '.'));
                });
            });
        }
    }

    [Theory]
    [InlineData(16, 1)]
    [InlineData(20, 1)]
    [InlineData(38, 2)]
    public void CoreTempAtIntegerScaleRepeatsEveryOriginalPixelAndGap(int size, int scale)
    {
        var source = size == 16 ? DotDigitSets.CoreTemp96 : DotDigitSets.CoreTemp120;
        var layout = DotDigitLayout.Fit(Sources("CoreTemp"), size, size);
        Assert.NotNull(layout);
        Assert.Same(source, layout.Glyphs);
        Assert.Equal((double)scale, layout.Scale);
        foreach (string number in Numbers)
        {
            var mask = layout.Draw(ParseNumber(number));
            Assert.Equal(source[0].Length * scale, mask.GetLength(0));
            for (int y = 0; y < mask.GetLength(0); y++)
            {
                string row = string.Join(".", number.Select(c => source[Characters.IndexOf(c)][y / scale]));
                Assert.Equal(row.SelectMany(c => Enumerable.Repeat(c == '#' ? byte.MaxValue : (byte)0, scale)),
                    Enumerable.Range(0, mask.GetLength(1)).Select(x => mask[y, x]));
            }
        }
    }

    private static int? ParseNumber(string number) =>
        number == "--" ? null : int.Parse(number, CultureInfo.InvariantCulture);

    [Fact]
    public void HeightLimitedFileEnlargesThreeDotsToFiveByNearestNeighbour()
    {
        string[][] source = Enumerable.Repeat(new[] { "#..", ".#.", "..#" }, 11).ToArray();
        var layout = DotDigitLayout.Fit([source], 32, 5);
        Assert.NotNull(layout);
        var mask = layout.Draw(8);
        // 3 → 5 では各軸で元の点を 2・1・2 列（行）ずつ採る。
        string[] expected = ["##...", "##...", "..#..", "...##", "...##"];
        Assert.Equal(expected.Length, mask.GetLength(0));
        for (int y = 0; y < expected.Length; y++)
            Assert.Equal(expected[y].Select(c => c == '#' ? byte.MaxValue : (byte)0),
                Enumerable.Range(0, mask.GetLength(1)).Select(x => mask[y, x]));
    }

    [Theory]
    [InlineData(17, 8)]
    [InlineData(18, 9)]
    [InlineData(19, 19)]
    public void IntermediateCoreTempFramesAlsoUse120Dpi(int width, int height)
    {
        var layout = DotDigitLayout.Fit(Sources("CoreTemp"), width, height);
        Assert.NotNull(layout);
        Assert.Same(DotDigitSets.CoreTemp120, layout.Glyphs);
        var one = layout.Draw(1);
        for (int y = 0; y < one.GetLength(0); y++)
            Assert.Equal(byte.MaxValue, one[y, one.GetLength(1) / 2]);

        var missing = layout.Draw(null);
        for (int y = 0; y < missing.GetLength(0); y++)
            for (int x = 0; x < missing.GetLength(1); x++)
            {
                bool onBar = y == missing.GetLength(0) / 2 && x != missing.GetLength(1) / 2;
                Assert.Equal(onBar ? byte.MaxValue : (byte)0, missing[y, x]);
            }
        foreach (string number in Numbers)
        {
            var mask = layout.Draw(ParseNumber(number));
            Assert.InRange(mask.GetLength(0), 1, height);
            Assert.InRange(mask.GetLength(1), 1, width);
        }
    }

    [Fact]
    public void FileThatCannotFitAtNativeSizeLeavesRoomForFallback()
    {
        string[][][] largeFile = [DotDigitSets.Claude];
        Assert.Null(DotDigitLayout.Fit(largeFile, 16, 8));
        Assert.Null(DotDigitLayout.Fit(largeFile, 16, 8, 999));
    }

    [Theory]
    [InlineData("CoreTemp")]
    [InlineData("Claude")]
    [InlineData("File")]
    public void WideThreeDigitPreviewsFitSmallIconsInTheirSelectedFont(string name)
    {
        var sources = Sources(name);
        foreach (int width in new[] { 16, 20 })
            foreach (int height in new[] { width, width / 2 })
            {
                var regular = DotDigitLayout.Fit(sources, width, height);
                Assert.NotNull(regular);
                foreach (int value in new[] { 150, 255, 888, 999 })
                {
                    var layout = DotDigitLayout.Fit(sources, width, height, value);
                    Assert.NotNull(layout);
                    Assert.Same(regular.Glyphs, layout.Glyphs);
                    var mask = layout.Draw(value);
                    Assert.InRange(mask.GetLength(0), 1, height);
                    Assert.InRange(mask.GetLength(1), 1, width);
                    var parts = value.ToString(CultureInfo.InvariantCulture).Select(c => layout.Draw(c - '0')).ToArray();
                    Assert.All(parts, digit => Assert.Contains(byte.MaxValue, digit.Cast<byte>()));
                    int space = mask.GetLength(1) - parts.Sum(digit => digit.GetLength(1));
                    Assert.Contains(Enumerable.Range(1, space - 1), gap => MatchesParts(mask, parts, gap, space - gap));
                }
            }
    }

    public static TheoryData<string, int, int> Frames
    {
        get
        {
            var result = new TheoryData<string, int, int>();
            foreach (string name in new[] { "CoreTemp", "Claude", "File" })
                foreach (int width in new[] { 16, 20, 24, 32, 40, 48, 64 })
                    foreach (int height in new[] { width, width / 2 })
                        result.Add(name, width, height);
            return result;
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void EveryValueFitsWithIdenticalDigitPixelsAndClearGaps(string name, int width, int height)
    {
        var sets = Sources(name);
        var layout = DotDigitLayout.Fit(sets, width, height);
        Assert.NotNull(layout);
        if (width > 20)
        {
            Assert.Same(sets.MaxBy(s => s[0].Length), layout.Glyphs);
            int widest = Numbers.Max(text => text.Sum(c => layout.Glyphs[Characters.IndexOf(c)][0].Length)
                + text.Length - 1);
            int nativeHeight = layout.Glyphs[0].Length;
            double expectedScale = Math.Min((double)width / widest, (double)height / nativeHeight);
            Assert.Equal(expectedScale, layout.Scale, 10);
            Assert.InRange(Math.Abs(layout.Draw(8).GetLength(0) - expectedScale * nativeHeight), 0, .5);
        }

        var digits = Enumerable.Range(0, 10).Select(n => layout.Draw(n)).ToArray();
        int rows = digits[0].GetLength(0);
        Assert.InRange(rows, 1, height);
        foreach (var digit in digits)
        {
            Assert.Equal(rows, digit.GetLength(0));
            Assert.Contains(byte.MaxValue, digit.Cast<byte>());
            Assert.All(digit.Cast<byte>(), ink => Assert.True(ink is 0 or byte.MaxValue));
        }
        // 桁の前後に何が来ても、単体の字を透明な字間で並べたものと全画素が一致する。
        foreach (string number in Numbers)
        {
            var mask = layout.Draw(ParseNumber(number));
            Assert.InRange(mask.GetLength(1), 1, width);
            Assert.Equal(rows, mask.GetLength(0));
            if (number == "--")
            {
                Assert.Contains(byte.MaxValue, mask.Cast<byte>());
                continue;
            }
            var parts = number.Select(c => digits[c - '0']).ToArray();
            int space = mask.GetLength(1) - parts.Sum(d => d.GetLength(1));
            Assert.True(space >= parts.Length - 1);
            if (parts.Length == 1) Assert.Equal(0, space);
            else if (parts.Length == 2) Assert.True(MatchesParts(mask, parts, space, 0));
            else Assert.Contains(Enumerable.Range(1, space - 1),
                firstGap => MatchesParts(mask, parts, firstGap, space - firstGap));
        }
    }

    private static bool MatchesParts(byte[,] mask, byte[][,] parts, int firstGap, int secondGap)
    {
        int left = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            int gap = i switch { 1 => firstGap, 2 => secondGap, _ => 0 };
            var digit = parts[i];
            for (int y = 0; y < mask.GetLength(0); y++)
            {
                for (int x = 0; x < gap; x++)
                    if (mask[y, left + x] != 0) return false;
                for (int x = 0; x < digit.GetLength(1); x++)
                    if (mask[y, left + gap + x] != digit[y, x]) return false;
            }
            left += gap + digit.GetLength(1);
        }
        return true;
    }
}
