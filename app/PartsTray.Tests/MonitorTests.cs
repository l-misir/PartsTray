using System.Buffers.Binary;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class MonitorTests
{
    [Fact]
    public void CpuIncludesIdleInKernelAndUsesDeltas() =>
        Assert.Equal(75, MonitorMath.CpuUsage(new(10, 20, 30), new(60, 120, 130), 0));

    [Theory]
    [InlineData(0, 0, 0, 42, 42)]
    [InlineData(300, 100, 100, 0, 0)]
    [InlineData(0, 100, 100, 0, 100)]
    [InlineData(0, 0, 0, -10, 0)]
    [InlineData(0, 0, 0, 150, 100)]
    public void CpuClampsAndKeepsPreviousForZeroDenominator(ulong idle, ulong kernel, ulong user, double previous, double expected) =>
        Assert.Equal(expected, MonitorMath.CpuUsage(default, new(idle, kernel, user), previous));

    [Fact]
    public void CpuCounterArithmeticDoesNotWrap()
    {
        Assert.Equal(100, MonitorMath.CpuUsage(new(100, 100, 100), new(50, 200, 200), 0));
        Assert.Equal(75, MonitorMath.CpuUsage(default, new(ulong.MaxValue / 2, ulong.MaxValue, ulong.MaxValue), 0), 8);
    }

    private static byte[] Temperatures(params float[] values)
    {
        byte[] bytes = new byte[4740];
        WriteUInt(bytes, 1536, (uint)values.Length); WriteUInt(bytes, 1540, 1);
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(1544 + i * 4), values[i]);
        return bytes;
    }
    private static void WriteUInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    [Fact]
    public void CoreTempAverageAndMaximum() => Assert.Equal(new CpuTemperature(55, 60), MonitorMath.ReadCoreTemp(Temperatures(50, 60)));

    [Fact]
    public void CoreTempDelta()
    {
        var bytes = Temperatures(40, 50); bytes[2685] = 1; WriteUInt(bytes, 1024, 100);
        Assert.Equal(new CpuTemperature(55, 60), MonitorMath.ReadCoreTemp(bytes));
    }

    [Fact]
    public void CoreTempFahrenheit()
    {
        var bytes = Temperatures(122, 140); bytes[2684] = 1;
        Assert.Equal(new CpuTemperature(55, 60), MonitorMath.ReadCoreTemp(bytes));
    }

    [Fact]
    public void CoreTempUsesEachCpuTjMaxBeforeFahrenheitConversion()
    {
        var bytes = Temperatures(90, 72, 108, 90);
        WriteUInt(bytes, 1536, 2); WriteUInt(bytes, 1540, 2);
        WriteUInt(bytes, 1024, 212); WriteUInt(bytes, 1028, 230);
        bytes[2685] = 1; bytes[2684] = 1;
        Assert.Equal(new CpuTemperature(55, 60), MonitorMath.ReadCoreTemp(bytes));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(300, 1)]
    [InlineData(2, 0)]
    [InlineData(128, 3)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    [InlineData(1, 129)]
    public void CoreTempRejectsInvalidCounts(uint cores, uint cpus)
    {
        var bytes = Temperatures(50, 60);
        WriteUInt(bytes, 1536, cores); WriteUInt(bytes, 1540, cpus);
        Assert.Null(MonitorMath.ReadCoreTemp(bytes));
    }

    [Fact]
    public void CoreTempSkipsInvalidValues()
    {
        Assert.Equal(new CpuTemperature(55, 60), MonitorMath.ReadCoreTemp(Temperatures(float.NaN, 200, 0, -1, 150, float.PositiveInfinity, float.NegativeInfinity, 50, 60)));
        Assert.Null(MonitorMath.ReadCoreTemp(Temperatures(float.NaN, 200, 0, 150)));
    }

    [Fact]
    public void CoreTempRequiresOnlyTheDocumentedPrefix()
    {
        var bytes = Temperatures(50, 60);
        Assert.Null(MonitorMath.ReadCoreTemp(bytes.AsSpan(0, 2687)));
        Assert.Equal(new CpuTemperature(55, 60), MonitorMath.ReadCoreTemp(bytes.AsSpan(0, 2688)));
    }

    [Fact]
    public void CoreTempSupports256Cores() =>
        Assert.Equal(new CpuTemperature(55, 55), MonitorMath.ReadCoreTemp(Temperatures(Enumerable.Repeat(55f, 256).ToArray())));

    [Theory]
    [InlineData(5, 3)]
    [InlineData(7, 4)]
    [InlineData(9, 5)]
    public void AllGlyphsHaveExpectedDimensionsAndInk(int height, int width)
    {
        foreach (char ch in "0123456789-")
        {
            var glyph = MonitorFont.Glyphs(ch.ToString(), height);
            Assert.Equal(height, glyph.GetLength(0)); Assert.Equal(width, glyph.GetLength(1));
            Assert.Contains(true, glyph.Cast<bool>());
        }
        var pair = MonitorFont.Glyphs("55", height);
        Assert.Equal(width * 2 + 1, pair.GetLength(1));
        for (int y = 0; y < height; y++) Assert.False(pair[y, width]);
    }

    [Theory]
    [InlineData(16, 7)]
    [InlineData(20, 9)]
    [InlineData(24, 9)]
    [InlineData(32, 9)]
    public void TempMemoryFontSize(int size, int expected) => Assert.Equal(expected, MonitorFont.TempMemoryHeight(size));

    // 11/19 実機：20px で高さ 5 は「ほぼ見えない」ため、20px 以上は 7、32px 以上は 9。
    [Theory]
    [InlineData(16, 5)]
    [InlineData(20, 7)]
    [InlineData(24, 7)]
    [InlineData(32, 9)]
    public void ParrotFontSize(int size, int expected) => Assert.Equal(expected, MonitorFont.ParrotHeight(size));

    [Fact]
    public void ThinStyleUsesTheTemperatureHeightWithoutStretching()
    {
        // Core Temp 風：20px で全部高さ 9、横に伸ばさない（"55" は 5＋1＋5 ＝ 11）。
        Assert.Equal(9, MonitorFont.SmallTextHeight(20, "thin"));
        Assert.Equal(7, MonitorFont.SmallTextHeight(20, "wide"));
        Assert.Equal(11, MonitorFont.Digits("55", 9, 20, "thin").GetLength(1));
        Assert.Equal(19, MonitorFont.Digits("55", 9, 20, "wide").GetLength(1));
    }

    [Theory]
    [InlineData(5, 9, new[] { 2, 2, 1, 2, 2 })]
    [InlineData(5, 5, new[] { 1, 1, 1, 1, 1 })]
    [InlineData(4, 9, new[] { 3, 2, 2, 2 })]
    [InlineData(3, 7, new[] { 3, 2, 2 })]
    public void ColumnWidthsGrowFromTheOutside(int baseWidth, int target, int[] expected) =>
        Assert.Equal(expected, MonitorFont.ColumnWidths(baseWidth, target));

    [Fact]
    public void WideGlyphsFillTheIconWidth()
    {
        // 20px・高さ 9 の 2 桁：1 文字 9 ドット＋間 1 ＝ 19。
        var wide = MonitorFont.GlyphsWide("55", 9, 20);
        Assert.Equal(19, wide.GetLength(1));
        Assert.Equal(9, wide.GetLength(0));
        // 「5」の上の横線は、伸ばした後も左端から右端までつながっている（形が崩れない）。
        for (int x = 0; x < 9; x++) Assert.True(wide[0, x]);
        for (int y = 0; y < 9; y++) Assert.False(wide[y, 9]);   // 文字の間
        // 3 桁は 1 文字 6 ドットまで：6×3＋2 ＝ 20。
        Assert.Equal(20, MonitorFont.GlyphsWide("100", 9, 20).GetLength(1));
        // 高さ 7 は 1 文字の幅 7 まで（縦横比 1:1）：7＋1＋7 ＝ 15。
        Assert.Equal(15, MonitorFont.GlyphsWide("23", 7, 20).GetLength(1));
        // 1 桁は 2 桁のときと同じ幅（9）。幅いっぱいには伸ばさない。
        Assert.Equal(9, MonitorFont.GlyphsWide("7", 9, 20).GetLength(1));
        // 幅が足りなければ伸ばさない。
        Assert.Equal(MonitorFont.Glyphs("55", 9).GetLength(1), MonitorFont.GlyphsWide("55", 9, 5).GetLength(1));
    }

    private static bool[,] Grid(params string[] rows)
    {
        var g = new bool[rows.Length, rows[0].Length];
        for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[0].Length; x++) g[y, x] = rows[y][x] == '#';
        return g;
    }
    private static string[] Rows(bool[,] g) =>
        Enumerable.Range(0, g.GetLength(0)).Select(y => string.Concat(Enumerable.Range(0, g.GetLength(1)).Select(x => g[y, x] ? '#' : '.'))).ToArray();

    [Fact]
    public void SolidFillsTheInsideAndKeepsInnerLinesAsHoles()
    {
        // 外枠の中は塗る。外に接していない内側の線（キーのような点）は穴として残す。
        var stroke = Grid(
            ".........",
            ".#######.",
            ".#.....#.",
            ".#.#.#.#.",
            ".#.....#.",
            ".#######.",
            ".........");
        Assert.Equal(new[]
        {
            ".........",
            ".#######.",
            ".#######.",
            ".##.#.##.",
            ".#######.",
            ".#######.",
            "........."
        }, Rows(PictureMask.Solid(stroke)));
        // 閉じていない線（弧だけ）は、中を塗らない。
        var arc = Grid(".....", ".###.", ".#.#.", ".#.#.", ".....");
        Assert.Equal(Rows(arc), Rows(PictureMask.Solid(arc)));
    }

    [Fact]
    public void AcpiKelvinsBecomeTheHottestZoneInCelsius()
    {
        // 11/19 実機の値：\_TZ.THRM 330.05 K ＝ 56.9℃。
        var one = MonitorMath.FromAcpi([330.05])!.Value;
        Assert.Equal(56.9, one.Average, 1); Assert.Equal(one.Average, one.Maximum);
        Assert.Equal(66.0, MonitorMath.FromAcpi([320.15, 339.15, 300.15])!.Value.Average, 1);
        // 0℃ 以下・150℃ 以上・NaN は捨てる。残らなければ温度なし。
        Assert.Equal(56.9, MonitorMath.FromAcpi([0, double.NaN, 500, 330.05])!.Value.Average, 1);
        Assert.Null(MonitorMath.FromAcpi([]));
        Assert.Null(MonitorMath.FromAcpi([273.15, double.NaN]));
    }

    [Theory]
    [InlineData("#FF3B30", true, 255, 59, 48)]
    [InlineData("#ffffff", true, 255, 255, 255)]
    [InlineData("FF3B30", false, 0, 0, 0)]
    [InlineData("#FF3B3", false, 0, 0, 0)]
    [InlineData("#GG0000", false, 0, 0, 0)]
    [InlineData(null, false, 0, 0, 0)]
    public void ColorsParse(string? text, bool ok, int r, int g, int b)
    {
        Assert.Equal(ok, MonitorColor.TryParse(text, out var c));
        if (ok) Assert.Equal(((byte)r, (byte)g, (byte)b), c);
    }

    [Theory]
    [InlineData(100, 20, 9, "100")]
    [InlineData(100, 17, 9, "100")]
    [InlineData(100, 16, 9, "99")]
    [InlineData(100, 16, 7, "100")]
    [InlineData(150, 16, 9, "99")]
    [InlineData(99, 20, 9, "99")]
    public void ThreeDigitsAreClampedOnlyWhenTooWide(int value, int size, int height, string expected) =>
        Assert.Equal(expected, MonitorFont.FitNumber(value, size, height));

    [Theory]
    [InlineData(0, 250)]
    [InlineData(5, 250)]
    [InlineData(50, 25)]
    [InlineData(100, 20)]
    [InlineData(-10, 250)]
    [InlineData(200, 20)]
    public void AnimationInterval(double cpu, int expected) => Assert.Equal(expected, MonitorMath.FrameInterval(cpu));

    [Fact]
    public void RainbowCyclesAndRoundsHalfUp()
    {
        Assert.Equal(MonitorMath.Rainbow(0, 0), MonitorMath.Rainbow(0, 3600));
        Assert.Equal(MonitorMath.Rainbow(1, 0), MonitorMath.Rainbow(0, 360));
        Assert.Equal((255, 26, 26), MonitorMath.Rainbow(0, 0));
        Assert.Equal(53, MonitorMath.Round(52.5));
    }
}
