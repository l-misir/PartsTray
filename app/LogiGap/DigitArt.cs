using System.Drawing.Imaging;
using System.Drawing.Text;
using LogiGap.Core;

namespace LogiGap;

// 通知領域のアイコンに描く数字の点。
// "wide" は LogiGap.Core の点の文字を横に伸ばしたもの。
// "thin" は Windows の「Tahoma」をにじませずに描く。Big は単独の大きな数字用。
// 11/19 実機で Core Temp の数字（「37」「58」「47」）を画面から切り出し、Tahoma 12〜13.5px（にじませない）と 1 ドットも違わないことを確かめた。
// 従来の 2 段・重ね描きは一辺 20px で 13.3px、16px で 10.7px。Big は「88」が収まる最大サイズ。
internal static class DigitArt
{
    private static readonly Dictionary<(string Text, int Size), bool[,]> cache = [];
    private static readonly Dictionary<int, (int Top, int Bottom)> rows = [];
    private static readonly Dictionary<int, float> bigSizes = [];
    private static readonly Dictionary<(string Text, float Pixels), bool[,]> bigCache = [];
    private static readonly Dictionary<float, (int Top, int Bottom)> bigRows = [];
    private static readonly object gate = new();

    public static bool[,] Big(int? value, int size, string style)
    {
        if (style != "thin")
        {
            int height = MonitorFont.SelectHeight(size, size);
            string text = value is { } n ? MonitorFont.FitNumber(n, size, height) : "--";
            return MonitorFont.Digits(text, height, size, "wide");
        }
        lock (gate)
        {
            float basePx = BigBasePixels(size);
            string text = value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "--";
            var mask = BigThin(text, basePx);
            if (mask.GetLength(1) > size)
            {
                // 3 桁（と、万一はみ出した 2 桁）は、その文字だけ小さくして幅に収める。
                float pixels = MonitorMath.LargestFit(px => Dimensions(BigThin(text, px)), size, size * 2f / 3f, basePx, .25f);
                mask = BigThin(text, pixels);
                if (value >= 100 && (mask.GetLength(1) > size || mask.GetLength(0) > size)) mask = BigThin("99", basePx);
            }
            return mask;
        }
    }

    // 1 桁・2 桁を同じ大きさで描くための基準の大きさ。
    // 11/20 実機（Claude のレビュー）：「88」だけで選ぶと 20.5px になるが、「49」は「4」の左の出っ張りで幅 21 になり、
    // アイコンの外に描こうとして起動時に落ちた。Tahoma の数字は送り幅が全部同じなので、
    // 「インクが一番左から始まる数字」＋「インクが一番右で終わる数字」の 2 桁が一番広い。その 2 桁が収まる大きさにする。
    internal static float BigBasePixels(int size)
    {
        lock (gate)
        {
            if (bigSizes.TryGetValue(size, out float pixels)) return pixels;
            float min = size * 2f / 3f;
            float upper = MonitorMath.LargestFit(px => Dimensions(BigThin("88", px)), size, min, size * 1.5f, .25f);
            return bigSizes[size] = MonitorMath.LargestFit(px => Dimensions(BigThin(WidestPair(px), px)), size, min, upper, .25f);
        }
    }

    // その大きさで一番幅の広くなる 2 桁（例："48"）。
    private static string WidestPair(float pixels)
    {
        char first = '0', last = '0';
        int bestLeft = int.MaxValue, bestRight = -1;
        for (char d = '0'; d <= '9'; d++)
        {
            var glyph = RenderPixels(d.ToString(), pixels);
            int left = int.MaxValue, right = -1;
            for (int y = 0; y < glyph.GetLength(0); y++)
                for (int x = 0; x < glyph.GetLength(1); x++)
                    if (glyph[y, x]) { left = Math.Min(left, x); right = Math.Max(right, x); }
            if (right < 0) continue;
            if (left < bestLeft) { bestLeft = left; first = d; }
            if (right > bestRight) { bestRight = right; last = d; }
        }
        return $"{first}{last}";
    }

    private static (int Width, int Height) Dimensions(bool[,] mask) => (mask.GetLength(1), mask.GetLength(0));

    private static bool[,] BigThin(string text, float pixels)
    {
        if (bigCache.TryGetValue((text, pixels), out var cached)) return cached;
        if (!bigRows.TryGetValue(pixels, out var r))
        {
            var zero = RenderPixels("0", pixels);
            int top = int.MaxValue, bottom = -1;
            for (int y = 0; y < zero.GetLength(0); y++)
                for (int x = 0; x < zero.GetLength(1); x++)
                    if (zero[y, x]) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            r = bottom < 0 ? (0, 0) : (top, bottom);
            bigRows[pixels] = r;
        }
        var full = RenderPixels(text, pixels);
        int left = int.MaxValue, right = -1;
        for (int y = 0; y < full.GetLength(0); y++)
            for (int x = 0; x < full.GetLength(1); x++)
                if (full[y, x]) { left = Math.Min(left, x); right = Math.Max(right, x); }
        var mask = new bool[r.Bottom - r.Top + 1, right < 0 ? 0 : right - left + 1];
        for (int y = r.Top; y <= r.Bottom; y++)
            for (int x = 0; x < mask.GetLength(1); x++) mask[y - r.Top, x] = full[y, left + x];
        if (bigCache.Count > 512) bigCache.Clear();
        return bigCache[(text, pixels)] = mask;
    }

    // 数字の高さ（点の行数）。
    public static int Height(int size, string style, int wideHeight)
    {
        if (style != "thin") return wideHeight;
        var (top, bottom) = DigitRows(size);
        return bottom - top + 1;
    }

    // 数を文字にする。3 桁が幅に収まらなければ "99"（ツールチップは正しい値）。
    public static string Fit(int value, int size, string style, int wideHeight)
    {
        if (style != "thin") return MonitorFont.FitNumber(value, size, wideHeight);
        string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return value >= 100 && Thin(text, size).GetLength(1) > size ? "99" : text;
    }

    public static bool[,] Mask(string text, int size, string style, int wideHeight) =>
        style != "thin" ? MonitorFont.Digits(text, wideHeight, size, style) : Thin(text, size);

    private static bool[,] Thin(string text, int size)
    {
        lock (gate)
        {
            if (cache.TryGetValue((text, size), out var cached)) return cached;
            var (top, bottom) = DigitRows(size);
            var full = Render(text, size);
            // 横は文字のある所だけに切り詰め、縦は「0」の上下にそろえる（「--」なども同じ帯の中に置く）。
            int left = int.MaxValue, right = -1;
            for (int y = 0; y < full.GetLength(0); y++)
                for (int x = 0; x < full.GetLength(1); x++)
                    if (full[y, x]) { left = Math.Min(left, x); right = Math.Max(right, x); }
            var mask = new bool[bottom - top + 1, right < 0 ? 0 : right - left + 1];
            for (int y = top; y <= bottom; y++)
                for (int x = 0; x < mask.GetLength(1); x++)
                    mask[y - top, x] = full[y, left + x];
            if (cache.Count > 512) cache.Clear();
            return cache[(text, size)] = mask;
        }
    }

    private static (int Top, int Bottom) DigitRows(int size)
    {
        lock (gate)
        {
            if (rows.TryGetValue(size, out var r)) return r;
            var zero = Render("0", size);
            int top = int.MaxValue, bottom = -1;
            for (int y = 0; y < zero.GetLength(0); y++)
                for (int x = 0; x < zero.GetLength(1); x++)
                    if (zero[y, x]) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            if (bottom < 0) { top = 0; bottom = 0; }
            return rows[size] = (top, bottom);
        }
    }

    private static bool[,] Render(string text, int size)
    {
        // 8 ポイント × (アイコンの一辺 × 6 dpi) ÷ 72 ＝ 一辺 × 2/3 ピクセル（16px → 96dpi、20px → 120dpi）。
        float pixels = size * 2f / 3f;
        return RenderPixels(text, pixels, size * 4, size * 2);
    }

    private static bool[,] RenderPixels(string text, float pixels)
    {
        int width = (int)Math.Ceiling(pixels * Math.Max(6, text.Length + 2)), height = (int)Math.Ceiling(pixels * 3);
        return RenderPixels(text, pixels, width, height);
    }

    private static bool[,] RenderPixels(string text, float pixels, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font("Tahoma", pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            graphics.DrawString(text, font, Brushes.White, 1, 1, StringFormat.GenericTypographic);
        }
        var mask = new bool[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                mask[y, x] = bitmap.GetPixel(x, y).A > 0;
        return mask;
    }
}
