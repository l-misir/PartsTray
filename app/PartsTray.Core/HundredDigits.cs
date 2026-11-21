// モニターの数字を、選んだ案の字形と大きさで枠に収めます。
using System.Globalization;

namespace PartsTray.Core;

public static class HundredDigits
{
    public static bool[,] Wide(int? value, int width, int height, string hundredStyle, bool digitPreview = false)
    {
        // 試し表示の shrink は、幅が足りなければ 3 桁を読める高さまで下げる。
        if ((hundredStyle == "uniform" || (digitPreview && hundredStyle == "shrink" && value >= 100)) &&
            value is >= 0 and <= 999)
        {
            int fittedHeight = MonitorFont.SelectHeight(width, height, digits: 3);
            return MonitorFont.GlyphsWide(value.Value.ToString(CultureInfo.InvariantCulture), fittedHeight, width, minimumDigits: 3);
        }
        var layout = TryLayout(value, width, hundredStyle,
            digit => MonitorFont.GlyphsWide(digit.ToString(), height, width));
        if (layout != null) return layout;
        string text = value is { } n ? MonitorFont.FitNumber(n, width, height) : "--";
        return MonitorFont.GlyphsWide(text, height, width);
    }

    // 通常の1文字ずつのマスクを受け取る。既定の描画を使う場合は null を返す。
    public static bool[,]? TryLayout(int? value, int width, string hundredStyle, Func<char, bool[,]> drawDigit)
        => TryLayout(value, width, hundredStyle, drawDigit, true);

    public static T[,]? TryLayout<T>(int? value, int width, string hundredStyle, Func<char, T[,]> drawDigit, T fullInk)
        where T : struct, IComparable<T>
    {
        if (value is not (>= 100 and <= 999) || hundredStyle is not ("narrow" or "narrowOne")) return null;
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 5);
        string text = value.Value.ToString(CultureInfo.InvariantCulture);
        var glyphs = text.Select(drawDigit).ToArray();
        int[] normal = glyphs.Select(glyph => glyph.GetLength(1)).ToArray();

        if (hundredStyle == "narrowOne")
        {
            int[] widths = normal.ToArray();
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '1') widths[i] = 1;
            int space = width - widths.Sum();
            if (space >= 0)
                return Compose(glyphs, widths, Math.Min(space, 2), text, fullInk);
        }

        // 各桁の左右にある縦線と、桁の間の1ドットを残す。
        return Compose(glyphs, FitWidths(text, normal, width - 2), 2, "", fullInk);
    }

    // 各桁の最低1ドットを先に確保し、残りを元の幅に応じて分ける。
    // 「100」の2つの「0」など、同じ数字には端数の1ドットも同じように配る。
    private static int[] FitWidths(string text, int[] normal, int available)
    {
        int total = normal.Sum();
        if (total <= available) return normal;
        int extra = available - normal.Length, weight = total - normal.Length;
        int[] fitted = normal.Select(w => 1 + (w - 1) * extra / weight).ToArray();
        int spare = available - fitted.Sum();
        var groups = Enumerable.Range(0, normal.Length).GroupBy(i => text[i])
            .OrderByDescending(group => (normal[group.First()] - 1) * extra % weight)
            .ThenByDescending(group => group.Count());
        foreach (var group in groups)
        {
            if (group.Count() > spare || fitted[group.First()] >= normal[group.First()]) continue;
            foreach (int i in group) fitted[i]++;
            spare -= group.Count();
        }
        return fitted;
    }

    // 字間に1ドットしか残らない場合は、先頭の桁を次の字から離す。
    private static T[,] Compose<T>(T[][,] glyphs, int[] widths, int space, string straightOnes, T fullInk)
        where T : struct, IComparable<T>
    {
        int height = glyphs[0].GetLength(0);
        var mask = new T[height, widths.Sum() + space];
        int left = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (straightOnes.Length > 0 && straightOnes[i] == '1')
            {
                for (int y = 0; y < height; y++) mask[y, left] = fullInk;
            }
            else
            {
                CopyGlyph(glyphs[i], mask, left, widths[i]);
            }
            left += widths[i] + (i < space ? 1 : 0);
        }
        return mask;
    }

    // 元の列の中心を横方向に写し、同じ列に来たインクを重ねる。
    // 全行で同じ写し方にすると、「1」の旗が付く行でも縦線は途切れない。
    private static void CopyGlyph<T>(T[,] glyph, T[,] mask, int left, int width) where T : struct, IComparable<T>
    {
        int sourceWidth = glyph.GetLength(1);
        for (int x = 0; x < sourceWidth; x++)
        {
            int column = left + (2 * x + 1) * width / (2 * sourceWidth);
            for (int y = 0; y < glyph.GetLength(0); y++)
                if (glyph[y, x].CompareTo(mask[y, column]) > 0) mask[y, column] = glyph[y, x];
        }
    }
}
