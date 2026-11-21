// 1 桁ずつのインクの幅を測り、指定範囲の数字を共通の大きさ・字間 1 ドットで描きます。
namespace PartsTray.Core;

public sealed class PackedDigits
{
    private readonly PackedDigits<bool> layout;
    private PackedDigits(PackedDigits<bool> layout) => this.layout = layout;

    public float Pixels => layout.Pixels;
    public bool[,] Draw(int value) => layout.Draw(value);

    // 2 桁の実幅は「1 字の幅 × 2 ＋ 字間 1」。まず正方形を大きい順に、
    // 次に高さだけ 1 ドット大きい候補を試す。最大桁数の値も同じ字幅で収める。
    public static IEnumerable<(int DigitWidth, int InkHeight)> Square2Sizes(int width, int height, int maximumValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumValue, 9);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumValue, 999);
        int digits = maximumValue >= 100 ? 3 : maximumValue >= 10 ? 2 : 1;
        int widthLimit = (width - (digits - 1)) / digits;
        for (int extraHeight = 0; extraHeight <= 1; extraHeight++)
        {
            int largest = Math.Min(widthLimit, (height - 1 - extraHeight) / 2);
            for (int digitWidth = largest; digitWidth >= 1; digitWidth--)
                yield return (digitWidth, 2 * digitWidth + 1 + extraHeight);
        }
    }

    public static PackedDigits Fit(int width, int height, int maximumValue, Func<char, float, bool[,]> drawDigit)
        => new(PackedDigits<bool>.Fit(width, height, maximumValue, drawDigit));

    public static PackedDigits? FitNative(int width, int height, int maximumValue, IEnumerable<int> sizes,
        Func<char, float, bool[,]> drawDigit)
        => PackedDigits<bool>.FitNative(width, height, maximumValue, sizes, drawDigit) is { } layout ? new(layout) : null;
}

// 2 値の字形と、アルファ値を持つなめらかな字形で同じインク幅と字間の決まりを使う。
public sealed class PackedDigits<T> where T : struct, IEquatable<T>
{
    private sealed record Glyph(T[,] Mask, int Left, int Top, int Width, int Bottom);

    private readonly Glyph[] glyphs;
    private readonly int top, height;
    private readonly int[] widths;
    private readonly T[][,] masks;

    private PackedDigits(float pixels, Glyph[] glyphs, int top, int height, int[] widths)
    {
        Pixels = pixels;
        this.glyphs = glyphs;
        this.top = top;
        this.height = height;
        this.widths = widths;
        masks = new T[widths.Length][,];
    }

    public float Pixels { get; }

    // drawDigit は各桁を同じ原点・ベースラインで描いたマスクを返す。既定値の画素は透明。
    // ヒンティングでドット数が段階的に変わるため、大きい方から 0.25 px 刻みで実測する。
    public static PackedDigits<T> Fit(int width, int height, int maximumValue, Func<char, float, T[,]> drawDigit)
    {
        ValidateFrame(width, height, maximumValue, drawDigit);
        for (float pixels = width; pixels >= 1; pixels -= .25f)
        {
            var layout = TryFit(width, height, maximumValue, pixels, drawDigit);
            if (layout != null) return layout;
        }
        throw new ArgumentOutOfRangeException(nameof(width), "すべての数字を描く領域が不足しています");
    }

    // ビットマップ書体は、書体に収録された原寸だけを候補にする。
    public static PackedDigits<T>? FitNative(int width, int height, int maximumValue, IEnumerable<int> sizes,
        Func<char, float, T[,]> drawDigit)
    {
        ValidateFrame(width, height, maximumValue, drawDigit);
        ArgumentNullException.ThrowIfNull(sizes);
        foreach (int pixels in sizes.OrderDescending())
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixels);
            var layout = TryFit(width, height, maximumValue, pixels, drawDigit);
            if (layout != null) return layout;
        }
        return null;
    }

    private static void ValidateFrame(int width, int height, int maximumValue, Func<char, float, T[,]> drawDigit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumValue, 9);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumValue, 999);
        ArgumentNullException.ThrowIfNull(drawDigit);
    }

    private static PackedDigits<T>? TryFit(int width, int height, int maximumValue, float pixels, Func<char, float, T[,]> drawDigit)
    {
        var glyphs = new Glyph[10];
        int top = int.MaxValue, bottom = -1;
        for (int digit = 0; digit <= 9; digit++)
        {
            var glyph = Measure(drawDigit((char)('0' + digit), pixels));
            if (glyph.Width <= 0) return null;
            glyphs[digit] = glyph;
            top = Math.Min(top, glyph.Top);
            bottom = Math.Max(bottom, glyph.Bottom);
            if (bottom - top + 1 > height) return null;
            // 「100」だけでも広すぎれば、残り 8 桁の試し描きは不要。
            if (digit == 1 && maximumValue >= 100 && glyph.Width + 2 * glyphs[0].Width + 2 > width)
                return null;
        }

        var widths = new int[maximumValue + 1];
        for (int value = 0; value <= maximumValue; value++)
        {
            int prefix = value < 10 ? 0 : widths[value / 10] + 1;
            widths[value] = prefix + glyphs[value % 10].Width;
            if (widths[value] > width) return null;
        }
        return new PackedDigits<T>(pixels, glyphs, top, bottom - top + 1, widths);
    }

    private static Glyph Measure(T[,] mask)
    {
        int left = mask.GetLength(1), right = -1, top = mask.GetLength(0), bottom = -1;
        for (int y = 0; y < mask.GetLength(0); y++)
            for (int x = 0; x < mask.GetLength(1); x++)
                if (!mask[y, x].Equals(default))
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
        return new Glyph(mask, left, top, right - left + 1, bottom);
    }

    public T[,] Draw(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, masks.Length);
        lock (masks)
        {
            if (masks[value] is { } cached) return cached;
            int left = widths[value], remaining = value;
            var mask = new T[height, left];
            do
            {
                var glyph = glyphs[remaining % 10];
                left -= glyph.Width;
                for (int y = glyph.Top; y <= glyph.Bottom; y++)
                    for (int x = 0; x < glyph.Width; x++)
                        mask[y - top, left + x] = glyph.Mask[y, glyph.Left + x];
                left--; // 左隣の数字との間に 1 列空ける。
                remaining /= 10;
            } while (remaining > 0);
            return masks[value] = mask;
        }
    }
}
