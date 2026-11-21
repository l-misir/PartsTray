// 通知領域の数字を、単独の大きな表示と2段・オウム用の表示に描き分けます。
using PartsTray.Core;

namespace PartsTray;

// 通知領域のアイコンに描く数字の点。
// "wide" は PartsTray.Core の点の文字を横に伸ばしたもの。
// "thin" は選んだ書体（既定 Tahoma）の濃淡を 0〜255 のマスクとして扱う。
// 11/19 実機で Core Temp の数字（「37」「58」「47」）を画面から切り出し、Tahoma 12〜13.5px（にじませない）と 1 ドットも違わないことを確かめた。
// 従来の 2 段・重ね描きは一辺 20px で 13.3px、16px で 10.7px。uniform は太字の各桁を詰めて描く。
internal static partial class DigitArt
{
    private static readonly Dictionary<(int Size, DigitFace Face), float> bigSizes = [];
    private static readonly Dictionary<(int Width, int Height, int MaximumValue, DigitFace Face), PackedDigits<byte>> uniformLayouts = [];
    private static readonly Dictionary<(string Text, float Pixels, DigitFace Face), byte[,]> fontMasks = [];
    private static readonly Dictionary<(float Pixels, DigitFace Face), (int Top, int Bottom)> fontRows = [];
    private static readonly object gate = new();

    public static byte[,] BigInk(int? value, int size, string style, string hundredStyle, int maximumValue = 100,
        string digitFont = MonitorSettings.DefaultDigitFont, bool plainZero = true, string[][][]? fileDigits = null,
        bool digitPreview = false) =>
        NumberInk(value, size, size, style, null, hundredStyle, maximumValue, digitFont, plainZero, fileDigits, digitPreview);

    public static byte[,] RowInk(int? value, int size, string style, int wideHeight, string hundredStyle, int maximumValue = 100,
        string digitFont = MonitorSettings.DefaultDigitFont, bool plainZero = true, string[][][]? fileDigits = null,
        bool digitPreview = false) =>
        NumberInk(value, size, size / 2, style, wideHeight, hundredStyle, maximumValue, digitFont, plainZero, fileDigits, digitPreview);

    private static byte[,] NumberInk(int? value, int width, int height, string style, int? wideHeight,
        string hundredStyle, int maximumValue, string digitFont, bool plainZero, string[][][]? fileDigits, bool digitPreview)
    {
        if (DigitFontSpec.DotSet(digitFont) is { } dots)
            return DotDigitArt.Draw(dots, fileDigits, value, width, height);
        if (style == "thin") return ThinNumber(value, width, height, hundredStyle, maximumValue, digitFont, plainZero, digitPreview);
        return OpaqueInk(HundredDigits.Wide(value, width, wideHeight ?? MonitorFont.SelectHeight(width, height), hundredStyle, digitPreview));
    }

    private static byte[,] ThinNumber(int? value, int width, int height, string hundredStyle,
        int maximumValue, string digitFont, bool plainZero, bool digitPreview)
    {
        bool uniform = hundredStyle == "uniform" && value is >= 0 and <= 999;
        var weight = uniform ? FontStyle.Bold : FontStyle.Regular;
        var face = ResolveFont(digitFont, weight) with { PlainZero = plainZero };
        if (face.Family == "Terminus")
        {
            if (value is null or (>= 0 and <= 999))
            {
                var layout = TerminusDigits.FindLayout(face.Weight, width, height, value > 150 ? 999 : 150,
                    face.Rendering, plainZero);
                if (layout != null) return value is { } n ? layout.Draw(n) : TerminusDigits.Missing(face.Weight, face.Rendering, layout);
            }
            // 原寸が入らない場合も明示した太さと描き方を引き継ぐ。名前だけなら従来の代替書体を使う。
            var spec = DigitFontSpec.Parse(digitFont);
            face = face with { Family = MonitorSettings.DefaultDigitFont,
                Weight = spec.Weight.HasValue || spec.Rendering.HasValue ? face.Weight : weight };
        }
        if (uniform)
        {
            // Pixel は CPU・温度・メモリとも、少なくとも 0〜150 を同じ大きさで表示する。
            if (face.Rendering == DigitRendering.Pixel || face.Square2) maximumValue = Math.Max(150, maximumValue);
            return UniformThin(value!.Value, width, height, maximumValue, face);
        }

        lock (gate)
        {
            bool single = height == width;
            float pixels = single ? BigBasePixels(width, face) : width * 2f / 3f;
            var layout = HundredDigits.TryLayout(value, width, hundredStyle,
                digit => FontMask(digit.ToString(), pixels, face), byte.MaxValue);
            if (layout != null) return layout;
            string text = value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "--";
            var mask = FontMask(text, pixels, face);
            if (digitPreview && value >= 100)
            {
                // 2 段・オウムでも、試したい 3 桁がその書体で収まるまで縮める。
                while (pixels > 1 && (mask.GetLength(1) > width || mask.GetLength(0) > height))
                {
                    pixels = Math.Max(1f, pixels - .25f);
                    mask = FontMask(text, pixels, face);
                }
                return mask;
            }
            if (mask.GetLength(1) <= width) return mask;
            if (single)
            {
                float fitted = MonitorMath.LargestFit(px => Dimensions(FontMask(text, px, face)), width, width * 2f / 3f, pixels, .25f);
                mask = FontMask(text, fitted, face);
            }
            return value >= 100 && (mask.GetLength(1) > width || mask.GetLength(0) > height) ? FontMask("99", pixels, face) : mask;
        }
    }

    private static byte[,] OpaqueInk(bool[,] mask)
    {
        var ink = new byte[mask.GetLength(0), mask.GetLength(1)];
        for (int y = 0; y < ink.GetLength(0); y++)
            for (int x = 0; x < ink.GetLength(1); x++)
                if (mask[y, x]) ink[y, x] = byte.MaxValue;
        return ink;
    }

    // 1 桁・2 桁を同じ大きさで描くための基準の大きさ。
    // 11/20 実機（Claude のレビュー）：「88」だけで選ぶと 20.5px になるが、「49」は「4」の左の出っ張りで幅 21 になり、
    // アイコンの外に描こうとして起動時に落ちた。Tahoma の数字は送り幅が全部同じなので、
    // 「インクが一番左から始まる数字」＋「インクが一番右で終わる数字」の 2 桁が一番広い。その 2 桁が収まる大きさにする。
    private static float BigBasePixels(int size, DigitFace face)
    {
        lock (gate)
        {
            if (bigSizes.TryGetValue((size, face), out float pixels)) return pixels;
            float min = size * 2f / 3f;
            float upper = MonitorMath.LargestFit(px => Dimensions(FontMask("88", px, face)), size, min, size * 1.5f, .25f);
            return bigSizes[(size, face)] = MonitorMath.LargestFit(px => Dimensions(FontMask(WidestPair(px, face), px, face)), size, min, upper, .25f);
        }
    }

    // その大きさで一番幅の広くなる 2 桁（例："48"）。
    private static string WidestPair(float pixels, DigitFace face)
    {
        char first = '0', last = '0';
        int bestLeft = int.MaxValue, bestRight = -1;
        for (char d = '0'; d <= '9'; d++)
        {
            var glyph = RenderPixels(d.ToString(), pixels, face);
            int left = int.MaxValue, right = -1;
            for (int y = 0; y < glyph.GetLength(0); y++)
                for (int x = 0; x < glyph.GetLength(1); x++)
                    if (glyph[y, x] != 0) { left = Math.Min(left, x); right = Math.Max(right, x); }
            if (right < 0) continue;
            if (left < bestLeft) { bestLeft = left; first = d; }
            if (right > bestRight) { bestRight = right; last = d; }
        }
        return $"{first}{last}";
    }

    private static (int Width, int Height) Dimensions(byte[,] mask) => (mask.GetLength(1), mask.GetLength(0));

    // CPU・メモリは 100、温度は 150 までを測る。枠と範囲が同じなら、各桁のインクも再利用する。
    private static byte[,] UniformThin(int value, int width, int height, int maximumValue, DigitFace face)
    {
        // 範囲外の 3 桁が渡っても、字を欠けさせず実値を描く。
        if (value > maximumValue) maximumValue = 999;
        var key = (width, height, maximumValue, face);
        lock (gate)
        {
            if (!uniformLayouts.TryGetValue(key, out var layout))
            {
                if (face.Square2) layout = Square2Digits.Fit(width, height, maximumValue, face);
                layout ??= PackedDigits<byte>.Fit(width, height, maximumValue, (digit, pixels) =>
                {
                    int side = (int)Math.Ceiling(pixels * 1.5f) + 4;
                    return RenderPixels(digit.ToString(), pixels, side, side,
                        face.Square2 ? face with { Rendering = DigitRendering.Pixel } : face);
                });
                uniformLayouts.Add(key, layout);
            }
            return layout.Draw(value);
        }
    }

    private static byte[,] FontMask(string text, float pixels, DigitFace face)
    {
        if (fontMasks.TryGetValue((text, pixels, face), out var cached)) return cached;
        if (!fontRows.TryGetValue((pixels, face), out var r))
        {
            fontRows[(pixels, face)] = r = InkRows(RenderPixels("0", pixels, face));
        }
        var mask = TrimToRows(RenderPixels(text, pixels, face), r);
        if (fontMasks.Count > 512) fontMasks.Clear();
        return fontMasks[(text, pixels, face)] = mask;
    }

    internal static (int Top, int Bottom) InkRows(byte[,] mask)
    {
        int top = int.MaxValue, bottom = -1;
        for (int y = 0; y < mask.GetLength(0); y++)
            for (int x = 0; x < mask.GetLength(1); x++)
                if (mask[y, x] != 0) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return bottom < 0 ? (0, 0) : (top, bottom);
    }

    internal static byte[,] TrimToRows(byte[,] full, (int Top, int Bottom) rows)
    {
        int left = int.MaxValue, right = -1;
        for (int y = 0; y < full.GetLength(0); y++)
            for (int x = 0; x < full.GetLength(1); x++)
                if (full[y, x] != 0) { left = Math.Min(left, x); right = Math.Max(right, x); }
        var mask = new byte[rows.Bottom - rows.Top + 1, right < 0 ? 0 : right - left + 1];
        for (int y = rows.Top; y <= rows.Bottom; y++)
            for (int x = 0; x < mask.GetLength(1); x++) mask[y - rows.Top, x] = full[y, left + x];
        return mask;
    }

    // 2段の中央にはできるだけ余白を置く。字が縦幅の半分ぴったりなら端にそろえる。
    public static int RowTop(int size, int height, bool lower, string? digitFont = null)
    {
        if (DigitFontSpec.DotSet(digitFont).HasValue)
        {
            int split = size / 2;
            return lower ? split + (size - split - height) / 2 : (split - height) / 2;
        }
        int row = (size - 1) / 2, centerOffset = (row - height) / 2;
        return lower ? Math.Min(size - height, size - row + centerOffset) : Math.Max(0, centerOffset);
    }

    // 上段の温度と下段のメモリは、それぞれの書体と表示範囲で独立に大きさを選ぶ。
    internal static (byte[,] Temperature, byte[,] Memory) TempMemoryInk(
        MonitorSettings settings, int? temperature, int? memory, int size, string[][][]? fileDigits = null,
        bool digitPreview = false)
    {
        int wideHeight = MonitorFont.TempMemoryHeight(size);
        string memoryFont = settings.ResolveDigitFont(settings.MemoryDigitFont);
        // 奇数幅では背景と同じく下段を 1 ドット高く取れる。従来の書体は従来の配置を使う。
        int memoryHeight = DigitFontSpec.DotSet(memoryFont).HasValue ? size - size / 2 : size / 2;
        return (
            RowInk(temperature, size, settings.DigitStyle, wideHeight, settings.HundredStyle,
                maximumValue: 150, digitFont: settings.ResolveDigitFont(settings.TemperatureDigitFont), plainZero: settings.PlainZero,
                fileDigits: fileDigits, digitPreview: digitPreview),
            NumberInk(memory, size, memoryHeight, settings.DigitStyle, wideHeight, settings.HundredStyle,
                maximumValue: 100, digitFont: memoryFont, plainZero: settings.PlainZero, fileDigits: fileDigits, digitPreview: digitPreview));
    }

    private static byte[,] RenderPixels(string text, float pixels, DigitFace face)
    {
        int width = (int)Math.Ceiling(pixels * Math.Max(6, text.Length + 2)), height = (int)Math.Ceiling(pixels * 3);
        return RenderPixels(text, pixels, width, height, face);
    }

    private static byte[,] RenderPixels(string text, float pixels, int width, int height, DigitFace face)
    {
        using var font = new Font(face.Family, pixels, face.Weight, GraphicsUnit.Pixel);
        return Rasterize(text, font, width, height, 1, face.Rendering, face.PlainZero);
    }
}
