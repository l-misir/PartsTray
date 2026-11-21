// 20 ドットの手描き見本を全画素で照合し、各サイズの字枠、「1」の中央配置、棒の点灯を確かめます。
using System.Globalization;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class SevenSegmentDigitsTests
{
    // 上・右上・右下・下・左下・左上・中を、点灯する順に a〜g で表す。
    private static readonly string[] Segments =
        ["abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg"];

    private readonly record struct Run(int Left, int Right)
    {
        public int Width => Right - Left + 1;
    }

    public static TheoryData<int, int> Frames
    {
        get
        {
            var frames = new TheoryData<int, int>();
            foreach (int size in Enumerable.Range(16, 25).Concat(new[] { 48, 64 }))
            {
                frames.Add(size, size);
                frames.Add(size, size / 2);
                if (size % 2 != 0) frames.Add(size, size - size / 2);
            }
            return frames;
        }
    }

    [Theory]
    [InlineData(8, false, "......########......", "......##....##......")]
    [InlineData(88, false, ".########..########.", ".##....##..##....##.")]
    [InlineData(11, false, "....##........##....", "....##........##....")]
    [InlineData(1, false, ".........##.........", ".........##.........")]
    [InlineData(188, false, ".##.#######.#######.", ".##.##...##.##...##.")]
    [InlineData(188, true, ".##..######..######.", ".##..##..##..##..##.")]
    public void TwentyDotMaskMatchesEveryPixelOfTheDrawing(int value, bool narrow, string bar, string sides)
    {
        var mask = narrow ? SevenSegmentDigits.Draw(value, 20, 20, narrow: true) : SevenSegmentDigits.Draw(value, 20, 20);
        if (value < 100)
            Assert.Equal(mask.Cast<byte>(), SevenSegmentDigits.Draw(value, 20, 20, narrow: true).Cast<byte>());
        Assert.Equal(18, mask.GetLength(0));
        Assert.Equal(20, mask.GetLength(1));
        for (int y = 0; y < 18; y++)
        {
            string row = y is 0 or 1 or 8 or 9 or 16 or 17 ? bar : sides;
            for (int x = 0; x < 20; x++)
                Assert.Equal(row[x] == '#' ? byte.MaxValue : (byte)0, mask[y, x]);
        }
    }

    [Theory]
    [InlineData(16, 16, 6, 2, 2, 5, 4)]
    [InlineData(20, 20, 8, 2, 2, 7, 6)]
    [InlineData(24, 24, 10, 2, 2, 9, 8)]
    [InlineData(32, 32, 14, 2, 3, 12, 10)]
    [InlineData(40, 40, 16, 6, 4, 15, 13)]
    [InlineData(16, 8, 4, 2, 1, 3, 3)]
    [InlineData(20, 10, 4, 2, 1, 4, 4)]
    [InlineData(24, 12, 4, 2, 1, 5, 5)]
    [InlineData(32, 16, 6, 2, 2, 7, 7)]
    [InlineData(40, 20, 8, 2, 2, 9, 9)]
    public void TwoAndThreeDigitsKeepTheScaledWidthsStrokesAndGaps(
        int width, int height, int pairWidth, int pairGap, int hundredWidth, int regularWidth, int narrowWidth)
    {
        foreach (bool narrow in new[] { false, true })
        {
            var pairMask = SevenSegmentDigits.Draw(88, width, height, narrow);
            Run[] pair = InkRuns(pairMask);
            Assert.Equal(2, pair.Length);
            Assert.Equal(pairWidth, pair[0].Width);
            Assert.Equal(pairGap, pair[1].Left - pair[0].Right - 1);
            AssertCentered(pair, width);
            Assert.Equal(hundredWidth, Enumerable.Range(pair[0].Left, pairWidth)
                .TakeWhile(x => pairMask[(height - 2) / 4, x] != 0).Count());
            var mask = SevenSegmentDigits.Draw(188, width, height, narrow);
            Run[] runs = InkRuns(mask);
            Assert.Equal(3, runs.Length);
            int digitWidth = narrow ? narrowWidth : regularWidth;
            Assert.Equal(hundredWidth, runs[0].Width);
            Assert.Equal(digitWidth, runs[1].Width);
            Assert.Equal(digitWidth, runs[2].Width);
            int gap = narrow ? hundredWidth : Math.Max(1, hundredWidth / 2);
            Assert.Equal(gap, runs[2].Left - runs[1].Right - 1);
            Assert.Equal(gap + (width - hundredWidth) % 2, runs[1].Left - runs[0].Right - 1);
            AssertCentered(runs, width);
            if (width == height) Assert.Equal(1, runs[0].Left);

            // 16 ドットの別案では、字幅 4 に穴を残せる太さは 1 ドット。
            int stroke = Math.Min(hundredWidth, (digitWidth - 1) / 2);
            Assert.Equal(stroke, Enumerable.Range(runs[1].Left, digitWidth)
                .TakeWhile(x => mask[(height - 2) / 4, x] != 0).Count());
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void AllDigitsUseTheCenteredSlotsForTheirNumberOfPlaces(int width, int height)
    {
        foreach (bool narrow in new[] { false, true })
        {
            Run[] single = InkRuns(SevenSegmentDigits.Draw(8, width, height, narrow));
            Run[] pair = InkRuns(SevenSegmentDigits.Draw(88, width, height, narrow));
            Run[] hundred = InkRuns(SevenSegmentDigits.Draw(188, width, height, narrow));
            Assert.Single(single);
            Assert.Equal(2, pair.Length);
            Assert.Equal(3, hundred.Length);
            AssertCentered(single, width);
            AssertCentered(pair, width);
            AssertCentered(hundred, width);
            Assert.Equal(single[0].Width, pair[0].Width);
            Assert.Equal(pair[0].Width, pair[1].Width);
            Assert.Equal(hundred[1].Width, hundred[2].Width);

            for (int value = 0; value < 200; value++)
            {
                string text = value.ToString(CultureInfo.InvariantCulture);
                var slots = text.Length switch { 1 => single, 2 => pair, _ => hundred };
                var mask = SevenSegmentDigits.Draw(value, width, height, narrow);
                Assert.Equal(height - 2, mask.GetLength(0));
                Assert.Equal(width, mask.GetLength(1));
                Run[] runs = InkRuns(mask);
                Assert.Equal(slots.Length, runs.Length);
                for (int i = 0; i < slots.Length; i++)
                {
                    if (text[i] == '1')
                    {
                        CheckSolidBar(mask, runs[i]);
                        int stroke = Math.Min(Math.Max(1, (int)Math.Round((height - 2) / 9.0)), (slots[i].Width - 1) / 2);
                        if (text.Length == 3 && i == 0) stroke = slots[i].Width;
                        if (text.Length == 1) stroke += (width - stroke) % 2;
                        Assert.Equal(stroke, runs[i].Width);
                        Assert.InRange(runs[i].Left, slots[i].Left, slots[i].Right);
                        Assert.InRange(runs[i].Right, slots[i].Left, slots[i].Right);
                        int slack = slots[i].Width - runs[i].Width;
                        bool biasRight = slots[i].Left + slots[i].Right < width - 1;
                        Assert.Equal(slots[i].Left + (slack + (biasRight ? 1 : 0)) / 2, runs[i].Left);
                        if (text.Length == 1) AssertCentered(runs, width);
                        if (text.Length == 3 && i == 0) Assert.Equal(slots[i], runs[i]);
                    }
                    else
                    {
                        Assert.Equal(slots[i], runs[i]);
                        CheckBars(mask, runs[i], text[i] - '0');
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void ZeroAndEightKeepAHoleBetweenTheirVerticalBars(int width, int height)
    {
        foreach (bool narrow in new[] { false, true })
        {
            // 桁幅が変わる単独・2 桁・百の位が 1 の場合をそれぞれ検査する。
            foreach (var (zeroValue, eightValue) in new[] { (0, 8), (90, 98), (100, 188) })
            {
                var zero = SevenSegmentDigits.Draw(zeroValue, width, height, narrow);
                var eight = SevenSegmentDigits.Draw(eightValue, width, height, narrow);
                var run = InkRuns(zero)[^1];
                Assert.Equal(run, InkRuns(eight)[^1]);
                int rows = height - 2, stroke = 0, center = (run.Left + run.Right) / 2;
                while (stroke < rows && zero[stroke, center] != 0) stroke++;
                Assert.InRange(stroke, 1, (run.Width - 1) / 2);
                int middle = (rows - stroke) / 2;
                for (int y = 0; y < rows; y++)
                    for (int x = run.Left; x <= run.Right; x++)
                    {
                        bool outline = y < stroke || y >= rows - stroke ||
                            x < run.Left + stroke || x > run.Right - stroke;
                        Assert.Equal(outline ? byte.MaxValue : (byte)0, zero[y, x]);
                        Assert.Equal(outline || (y >= middle && y < middle + stroke) ? byte.MaxValue : (byte)0, eight[y, x]);
                    }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void ValuesOutsideTheHundredsLayoutUseEqualWidthSlots(int width, int height)
    {
        foreach (bool narrow in new[] { false, true })
            foreach (int? value in new int?[] { null, -1, -8, -11, -88, -100, -888, 200, 201, 211, 255, 888, 999 })
            {
                CheckFittedValue(value, width, height, narrow);
            }
    }

    [Theory]
    [InlineData(-12345, 32, 16)]
    [InlineData(12345678, 40, 40)]
    [InlineData(int.MinValue, 64, 32)]
    [InlineData(int.MaxValue, 64, 64)]
    public void LongerValuesKeepEveryDigitWhenThereIsRoom(int value, int width, int height)
    {
        CheckFittedValue(value, width, height, narrow: false);
        CheckFittedValue(value, width, height, narrow: true);
    }

    private static void CheckFittedValue(int? value, int width, int height, bool narrow)
    {
        string text = value?.ToString(CultureInfo.InvariantCulture) ?? "--";
        var mask = SevenSegmentDigits.Draw(value, width, height, narrow);
        Assert.Equal(height - 2, mask.GetLength(0));
        Assert.Equal(width, mask.GetLength(1));
        Run[] runs = InkRuns(mask);
        Assert.Equal(text.Length, runs.Length);
        AssertCentered(runs, width);
        int digit = text.IndexOfAny("023456789".ToCharArray());
        int ordinaryWidth = digit >= 0 ? runs[digit].Width : runs[0].Width;
        for (int i = 0; i < runs.Length; i++)
        {
            if (text[i] == '1')
            {
                CheckSolidBar(mask, runs[i]);
                continue;
            }
            if (text[i] == '-')
            {
                // 16 ドットでは、負号と数字 3 字を残すため負号を短くする。
                if (width == 16 && text.Length == 4) Assert.InRange(runs[i].Width, 1, ordinaryWidth);
                else Assert.Equal(ordinaryWidth, runs[i].Width);
                CheckMinus(mask, runs[i]);
            }
            else
            {
                Assert.Equal(ordinaryWidth, runs[i].Width);
                CheckBars(mask, runs[i], text[i] - '0');
            }
        }
    }

    private static void AssertCentered(Run[] runs, int width)
    {
        Assert.InRange(runs[0].Left, 1, width / 2);
        Assert.Equal(runs[0].Left, width - 1 - runs[^1].Right);
    }

    // 描画側の配置計算を参照せず、実際に点灯する連続した列を取り出す。
    private static Run[] InkRuns(byte[,] mask)
    {
        var runs = new List<Run>();
        int start = -1;
        for (int x = 0; x <= mask.GetLength(1); x++)
        {
            bool lit = false;
            if (x < mask.GetLength(1))
                for (int y = 0; y < mask.GetLength(0) && !lit; y++) lit = mask[y, x] != 0;
            if (lit && start < 0) start = x;
            if (!lit && start >= 0)
            {
                runs.Add(new Run(start, x - 1));
                start = -1;
            }
        }
        return runs.ToArray();
    }

    private static void CheckSolidBar(byte[,] mask, Run run)
    {
        for (int y = 0; y < mask.GetLength(0); y++)
            for (int x = run.Left; x <= run.Right; x++) Assert.Equal(byte.MaxValue, mask[y, x]);
    }

    private static void CheckMinus(byte[,] mask, Run run)
    {
        int rows = mask.GetLength(0);
        int stroke = Enumerable.Range(0, rows).Count(y => mask[y, run.Left] != 0);
        Assert.InRange(stroke, 1, (rows - 2) / 3);
        int middle = (rows - stroke) / 2;
        for (int y = 0; y < rows; y++)
            for (int x = run.Left; x <= run.Right; x++)
                Assert.Equal(y >= middle && y < middle + stroke ? byte.MaxValue : (byte)0, mask[y, x]);
    }

    private static void CheckBars(byte[,] mask, Run run, int digit)
    {
        int rows = mask.GetLength(0), center = (run.Left + run.Right) / 2;
        bool[] lit = [mask[0, center] != 0, mask[rows / 4, run.Right] != 0,
            mask[3 * rows / 4, run.Right] != 0, mask[rows - 1, center] != 0,
            mask[3 * rows / 4, run.Left] != 0, mask[rows / 4, run.Left] != 0,
            mask[(rows - 1) / 2, center] != 0 || mask[rows / 2, center] != 0];
        for (int i = 0; i < lit.Length; i++)
            Assert.Equal(Segments[digit].Contains((char)('a' + i)), lit[i]);
    }
}
