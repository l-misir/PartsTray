// GDI の白黒の字形から、字間を含む 2 桁のインクが正方形になる最大の組み合わせを選びます。
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal static class Square2Digits
{
    private sealed record Glyph(byte[,] Ink, int Baseline);

    internal static PackedDigits<byte>? Fit(int width, int height, int maximumValue, DigitArt.DigitFace face)
    {
        nint dc = CreateCompatibleDC(0);
        if (dc == 0) return null;
        try
        {
            foreach (var (digitWidth, inkHeight) in PackedDigits.Square2Sizes(width, height, maximumValue))
            {
                // lfHeight は em 高で指定する。枠に収まるかはベースラインからの実インクで判定する。
                PackedDigits<byte>? best = null;
                int fewestCorrections = int.MaxValue;
                for (int em = inkHeight * 3; em >= 1; em--)
                {
                    var glyphs = TryFont(dc, face, em, digitWidth, inkHeight, out int corrections);
                    if (glyphs == null || corrections >= fewestCorrections) continue;
                    var layout = PackedDigits<byte>.FitNative(width, height, maximumValue, [em],
                        (digit, _) => glyphs[digit - '0']);
                    if (layout == null) continue;
                    if (corrections == 0) return layout;
                    best = layout;
                    fewestCorrections = corrections;
                }
                if (best != null) return best;
            }
            return null;
        }
        finally { DeleteDC(dc); }
    }

    private static byte[][,]? TryFont(nint dc, DigitArt.DigitFace face, int em, int width, int height,
        out int corrections)
    {
        corrections = 0;
        // CreateFontW の先頭 2 引数が LOGFONT.lfHeight / lfWidth。QUALITY=3 は NONANTIALIASED_QUALITY。
        nint font = CreateFontW(-em, width, 0, 0, face.Weight == FontStyle.Bold ? 700 : 400,
            0, 0, 0, 1, 4, 0, 3, 0, face.Family);
        if (font == 0) return null;
        nint oldFont = SelectObject(dc, font);
        try
        {
            if (oldFont == 0 || oldFont == -1) return null;
            var zero = ReadGlyph(dc, '0', 1, height);
            if (zero == null) return null;
            var glyphs = new byte[10][,];
            for (int digit = 0; digit < 10; digit++)
            {
                var ink = FitGlyph(dc, (char)('0' + digit), width, height, zero.Baseline);
                if (ink == null) return null;
                if (digit == 0 && face.PlainZero) ink = PlainZero.Clean(ink);
                if (ink.GetLength(1) == width - 1)
                {
                    ink = AddOneColumn(ink);
                    corrections++;
                }
                glyphs[digit] = ink;
            }
            return glyphs;
        }
        finally
        {
            if (oldFont != 0 && oldFont != -1) SelectObject(dc, oldFont);
            DeleteObject(font);
        }
    }

    // 同じ lfHeight / lfWidth を使い、細い「1」などは輪郭を GDI に渡す段階で横方向だけ微調整する。
    // lfWidth 相当の 1/16 ドット刻みで原寸に近い順に試す。幅の判定には送り幅や左右の余白を含めない。
    private static byte[,]? FitGlyph(nint dc, char digit, int width, int height, int baseline)
    {
        byte[,]? narrower = null;
        int unit = 16 * width;
        for (int distance = 0; distance <= 2 * unit; distance++)
        {
            if (distance < unit && Probe(unit - distance) is { } small) return small;
            if (distance > 0 && Probe(unit + distance) is { } large) return large;
        }
        return narrower;

        byte[,]? Probe(int scale)
        {
            var glyph = ReadGlyph(dc, digit, (double)scale / unit, height, width);
            if (glyph == null || glyph.Baseline != baseline) return null;
            int measured = glyph.Ink.GetLength(1);
            if (measured == width - 1) narrower ??= glyph.Ink;
            return measured == width ? glyph.Ink : null;
        }
    }

    // ヒンティングで幅が 3→5 などと飛ぶ場合だけ、1 列狭い字のインクの少ない列を複製する。
    // 「1」の長い縦線を太らせず、細い横線や端の 1 ドットも落とさない。
    private static byte[,] AddOneColumn(byte[,] ink)
    {
        int width = ink.GetLength(1), height = ink.GetLength(0), repeat = 0, least = height + 1;
        for (int x = 0; x < width; x++)
        {
            int count = 0;
            for (int y = 0; y < height; y++)
                if (ink[y, x] != 0) count++;
            if (count < least) { repeat = x; least = count; }
        }
        var result = new byte[height, width + 1];
        for (int y = 0; y < height; y++)
            for (int x = 0; x <= width; x++) result[y, x] = ink[y, x <= repeat ? x : x - 1];
        return result;
    }

    private static Glyph? ReadGlyph(nint dc, char digit, double scale, int targetHeight, int? targetWidth = null)
    {
        // MAT2 の各成分は 16.16 固定小数点。GGO_BITMAP は行ごとに DWORD 境界にそろえた 1 bit の字形。
        var matrix = new GlyphMatrix((int)Math.Round(scale * 65536), 0, 0, 65536);
        uint size = GetGlyphOutlineW(dc, digit, 1, out var metrics, 0, null, in matrix);
        if (size is 0 or uint.MaxValue || metrics.Height < targetHeight ||
            (targetWidth is { } w && metrics.Width < w - 1)) return null;
        var bits = new byte[size];
        if (GetGlyphOutlineW(dc, digit, 1, out metrics, size, bits, in matrix) == uint.MaxValue) return null;
        int stride = (metrics.Width + 31) / 32 * 4;
        bool IsInk(int x, int y) => (bits[y * stride + x / 8] & (0x80 >> (x % 8))) != 0;
        int left = metrics.Width, right = -1, top = metrics.Height, bottom = -1;
        for (int y = 0; y < metrics.Height; y++)
            for (int x = 0; x < metrics.Width; x++)
                if (IsInk(x, y))
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        int width = right - left + 1;
        if (width <= 0 || bottom - top + 1 != targetHeight ||
            (targetWidth is { } target && width != target && width != target - 1)) return null;
        var mask = new byte[targetHeight, width];
        for (int y = 0; y < targetHeight; y++)
            for (int x = 0; x < width; x++)
                if (IsInk(left + x, top + y)) mask[y, x] = 255;
        return new Glyph(mask, metrics.OriginY - top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct GlyphMatrix(int Xx, int Xy, int Yx, int Yy);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct GlyphMetrics(int Width, int Height, int OriginX, int OriginY, short AdvanceX, short AdvanceY);

    [DllImport("gdi32")] private static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")] private static extern nint SelectObject(nint dc, nint handle);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
    [DllImport("gdi32", ExactSpelling = true)]
    private static extern uint GetGlyphOutlineW(nint dc, uint character, uint format, out GlyphMetrics metrics,
        uint bufferSize, [Out] byte[]? buffer, in GlyphMatrix matrix);
}
