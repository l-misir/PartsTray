// Square2 の候補を全表示値の実幅と照らし合わせ、最大の正方形と次善の縦長が選ばれる順番を確かめます。
using System.Globalization;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class Square2SizesTests
{
    [Theory]
    [InlineData(16, 16, 150, 9)]
    [InlineData(20, 20, 150, 13)]
    [InlineData(24, 24, 150, 15)]
    [InlineData(32, 32, 150, 21)]
    [InlineData(16, 8, 150, 7)]
    [InlineData(20, 10, 150, 9)]
    [InlineData(24, 12, 150, 11)]
    [InlineData(32, 16, 150, 15)]
    [InlineData(20, 20, 99, 19)]
    [InlineData(20, 20, 100, 13)]
    [InlineData(5, 4, 150, 3)]
    public void LargestSquareComesFirstThenEveryRealizableFallback(int width, int height, int maximum, int largestSide)
    {
        var candidates = PackedDigits.Square2Sizes(width, height, maximum).ToArray();
        Assert.Equal(largestSide, candidates[0].InkHeight);

        // 各設定で、字幅 1 から枠幅まで試し、範囲の全値が字間 1 ドットで入るものだけを残す。
        var lengths = Enumerable.Range(0, maximum + 1).Select(n => n.ToString(CultureInfo.InvariantCulture).Length).ToArray();
        var expected = new List<(int DigitWidth, int InkHeight)>();
        foreach (int difference in new[] { 0, 1 })
        {
            for (int digitWidth = width; digitWidth >= 1; digitWidth--)
            {
                int inkHeight = 2 * digitWidth + 1 + difference;
                if (inkHeight <= height && lengths.All(length => length * digitWidth + length - 1 <= width))
                    expected.Add((digitWidth, inkHeight));
            }
        }
        Assert.Equal(expected, candidates);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(16, 2)]
    public void NoCandidateWhenThreeDigitsOrTheTwoDigitHeightCannotFit(int width, int height) =>
        Assert.Empty(PackedDigits.Square2Sizes(width, height, 150));
}
