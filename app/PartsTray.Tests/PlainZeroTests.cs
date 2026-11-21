// 0 の中身だけが消え、輪郭の太さ・濃淡・元の書体データが保たれることを確認します。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class PlainZeroTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearsDetachedDotOrSlashAndPreservesTheSource(bool diagonal)
    {
        var outline = Mask(
            ".#####.",
            "#.....#",
            "#.....#",
            "#.....#",
            "#.....#",
            "#.....#",
            ".#####.");
        var decorated = (bool[,])outline.Clone();
        decorated[3, 3] = true;
        if (diagonal) decorated[4, 2] = decorated[2, 4] = true;

        var plain = PlainZero.Clean(decorated);
        Assert.Equal(outline.Cast<bool>(), plain.Cast<bool>());
        Assert.True(decorated[3, 3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovesTheConnectedSlashInBundledTerminusWithoutThinningItsSides(bool bold)
    {
        // 同梱 Regular/Bold の 14px・16px にある字形。斜線の両端は左右の枠と一体。
        string side = bold ? "##...##" : "#....#";
        string cap = bold ? ".#####." : ".####.";
        string[] middle = bold ? ["##..###", "##.####", "####.##", "###..##"]
                               : ["#...##", "#..#.#", "#.#..#", "##...#"];
        var decorated = Mask([cap, side, side, .. middle, side, side, cap]);
        var expected = Mask([cap, side, side, side, side, side, side, side, side, cap]);

        Assert.Equal(expected.Cast<bool>(), PlainZero.Clean(decorated).Cast<bool>());
    }

    [Fact]
    public void RetainsAntialiasCoverageOfTheOutline()
    {
        byte[,] glyph = {
            { 0, 35, 175, 240, 175, 35, 0 },
            { 76, 202, 0, 0, 0, 202, 76 },
            { 76, 202, 0, 0, 0, 202, 76 },
            { 76, 202, 0, 185, 0, 202, 76 },
            { 76, 202, 0, 0, 0, 202, 76 },
            { 76, 202, 0, 0, 0, 202, 76 },
            { 0, 35, 175, 240, 175, 35, 0 },
        };
        var expected = (byte[,])glyph.Clone();
        expected[3, 3] = 0;
        Assert.Equal(expected.Cast<byte>(), PlainZero.Clean(glyph).Cast<byte>());
        Assert.Equal(185, glyph[3, 3]);
    }

    [Fact]
    public void LeavesPlainAndOpenContoursAndExteriorMarksAlone()
    {
        var plain = Mask(
            ".....#.",
            ".###...",
            "#...#..",
            "#...#..",
            ".###...");
        Assert.Equal(plain.Cast<bool>(), PlainZero.Clean(plain).Cast<bool>());
        plain[1, 2] = false;
        plain[3, 2] = true;
        Assert.Equal(plain.Cast<bool>(), PlainZero.Clean(plain).Cast<bool>());
        Assert.Empty(PlainZero.Clean(new bool[2, 0]).Cast<bool>());
    }

    private static bool[,] Mask(params string[] rows)
    {
        var mask = new bool[rows.Length, rows[0].Length];
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                mask[y, x] = rows[y][x] == '#';
        return mask;
    }
}
