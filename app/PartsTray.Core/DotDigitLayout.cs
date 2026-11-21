// 固定の点の絵を同じ倍率で最近傍拡大し、0〜150 と欠測の字間まで枠に収めます。
using System.Globalization;

namespace PartsTray.Core;

public sealed class DotDigitLayout
{
    public string[][] Glyphs { get; }
    public double Scale { get; }
    private readonly int frameWidth;
    private readonly int[] nativeWidths;
    private readonly byte[][,] digits;

    private DotDigitLayout(string[][] glyphs, double scale, int width, int[] native, int[] scaled)
    {
        Glyphs = glyphs;
        Scale = scale;
        frameWidth = width;
        nativeWidths = native;
        int height = Rounded(glyphs[0].Length * scale);
        digits = glyphs.Select((rows, i) => Resize(rows, native[i], scaled[i], height)).ToArray();
    }

    public static DotDigitLayout? Fit(string[][][] sets, int width, int height, int? value = null)
    {
        if (width < 1 || height < 1) return null;
        string? extra = value is < 0 or > 150 ? NumberText(value) : null;
        // 大きいアイコンは原画を固定する。小さい枠では従来どおり原寸が収まる組を使う。
        var sources = sets.OrderByDescending(g => g[0].Length);
        foreach (var glyphs in width > 20 ? sources.Take(1) : sources)
        {
            if (glyphs == DotDigitSets.CoreTemp96 && width > 16) continue;
            int[] native = glyphs.Select(rows => rows[0].Length).ToArray();
            int widest = Widest(native, 1, extra);
            if (width <= 16 && widest > width)
            {
                // 原画の「1」に左余白がある場合は、幅を超えた分だけ空白の列を除く。
                int trim = 0;
                while (trim < widest - width && trim < native[1] - 1 && glyphs[1].All(row => row[trim] == '.'))
                    trim++;
                native[1] -= trim;
                widest = Widest(native, 1, extra);
            }
            double scale = Math.Min((double)width / widest, (double)height / glyphs[0].Length);
            // 17〜19 ドットの中間 DPI も 120 DPI 原画を使う。段が低ければ少し縮める。
            bool intermediate = glyphs == DotDigitSets.CoreTemp120 && width is > 16 and < 20;
            // 通常の 0〜150 が原寸で入る組なら、追加の 3 桁だけ幅が足りない場合もその組を使う。
            bool fitsNormalRange = extra != null && glyphs[0].Length <= height && Widest(native, 1, null) <= width;
            if (scale < 1 && !intermediate && !fitsNormalRange) continue;
            if (width <= 20 && scale >= 1) scale = Math.Floor(scale);
            int[]? scaled = FitWidths(native, scale, width, extra);
            if (scaled != null) return new DotDigitLayout(glyphs, scale, width, native, scaled);
        }
        return null;
    }

    private static int[]? FitWidths(int[] native, double scale, int width, string? extra)
    {
        int gap = Math.Max(1, (int)scale);
        var scaled = native.Select(w => Math.Max(1, (int)(w * scale))).ToArray();
        if (Widest(scaled, gap, extra) > width) return null;
        // 余った画素は端数の大きい字幅から配る。同じ原寸幅にはどの桁でも同じ列数を割り当てる。
        foreach (int w in native.Distinct().OrderByDescending(w => w * scale % 1))
        {
            if (w * scale % 1 < .5) break;
            int target = Rounded(w * scale);
            var candidate = native.Select((n, i) => n == w ? target : scaled[i]).ToArray();
            if (Widest(candidate, gap, extra) <= width) scaled = candidate;
        }
        return scaled;
    }

    private static int Widest(int[] widths, int gap, string? extra)
    {
        int widest = 2 * widths[10] + gap; // 欠測時の「--」
        Span<int> numbers = stackalloc int[151];
        for (int n = 0; n < numbers.Length; n++)
        {
            numbers[n] = widths[n % 10] + (n < 10 ? 0 : numbers[n / 10] + gap);
            widest = Math.Max(widest, numbers[n]);
        }
        return extra == null ? widest : Math.Max(widest, TextWidth(extra, widths, gap));
    }

    public byte[,] Draw(int? value)
    {
        string text = NumberText(value);
        int gaps = text.Length - 1;
        int glyphWidth = 0;
        foreach (char c in text) glyphWidth += digits[GlyphIndex(c)].GetLength(1);
        int target = Math.Min(frameWidth, Rounded(TextWidth(text, nativeWidths, 1) * Scale));
        int spacing = Math.Clamp(target - glyphWidth, gaps * Math.Max(1, (int)Scale),
            gaps * (int)Math.Ceiling(Scale));
        int height = digits[0].GetLength(0);
        var ink = new byte[height, glyphWidth + spacing];
        int previousWidths = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var digit = digits[GlyphIndex(text[i])];
            int w = digit.GetLength(1);
            // 字間の丸め誤差だけを分配し、同じ数字は並び順にかかわらず同じ絵を写す。
            int left = previousWidths + (gaps == 0 ? 0 : (i * spacing + gaps / 2) / gaps);
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(digit, y * w, ink, y * ink.GetLength(1) + left, w);
            previousWidths += w;
        }
        return ink;
    }

    private static byte[,] Resize(string[] rows, int nativeWidth, int width, int height)
    {
        var digit = new byte[height, width];
        int start = rows[0].Length - nativeWidth;
        for (int y = 0; y < height; y++)
        {
            string row = rows[Sample(y, rows.Length, height)];
            for (int x = 0; x < width; x++)
                if (row[start + Sample(x, nativeWidth, width)] == '#') digit[y, x] = byte.MaxValue;
        }
        return digit;
    }

    // 拡大は各画素の中心で採取する。微縮小では先頭の点を基準にし、1 の芯と欠測の中央線を残す。
    private static int Sample(int i, int source, int target) => target < source
        ? (int)(((long)i * source + (target - 1) / 2) / target)
        : (int)(((2L * i + 1) * source) / (2L * target));

    private static int Rounded(double n) => Math.Max(1, (int)Math.Round(n, MidpointRounding.AwayFromZero));
    private static string NumberText(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "--";
    private static int GlyphIndex(char c) => c == '-' ? 10 : c - '0';
    private static int TextWidth(string text, int[] widths, int gap)
    {
        int width = (text.Length - 1) * gap;
        foreach (char c in text) width += widths[GlyphIndex(c)];
        return width;
    }
}
