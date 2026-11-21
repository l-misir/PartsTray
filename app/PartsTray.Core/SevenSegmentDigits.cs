// アイコンや段の実寸から、数字を構成する 7 本の棒を整数ドットの長方形として描きます。
using System.Globalization;

namespace PartsTray.Core;

public static class SevenSegmentDigits
{
    // 上から時計回りに A〜F。G は中央の横棒。
    private const byte A = 1, B = 2, C = 4, D = 8, E = 16, F = 32, G = 64;
    private static readonly byte[] segments =
    [
        A | B | C | D | E | F,     // 0
        B | C,                     // 1
        A | B | G | E | D,         // 2
        A | B | G | C | D,         // 3
        F | G | B | C,             // 4
        A | F | G | C | D,         // 5
        A | F | G | E | C | D,     // 6
        A | B | C,                 // 7
        A | B | C | D | E | F | G, // 8
        A | B | C | D | F | G,     // 9
        G                          // -
    ];

    // マスクは左右の余白を含む枠幅。上下の各 1 ドットは呼び出し側で取る。
    public static byte[,] Draw(int? value, int width, int height, bool narrow = false)
    {
        if (width < 5 || height < 7) return new byte[0, 0];
        int inkHeight = height - 2, available = width - 2;
        int stroke = Math.Max(1, (int)Math.Round(inkHeight / 9.0));
        int heightLimit = (inkHeight + 1) / 2;
        var (pairWidth, pairGap) = FitPair(available, heightLimit, stroke);
        string text = value?.ToString(CultureInfo.InvariantCulture) ?? "--";
        if (value is < 0 or >= 200)
            return DrawFittedNumber(text, width, inkHeight, pairWidth, stroke);

        bool hundred = value is >= 100 and < 200;
        int gap = hundred ? (narrow ? stroke : Math.Max(1, stroke / 2)) : pairGap;
        int digitWidth = hundred
            ? Math.Min(heightLimit, (available - stroke - 2 * gap) / 2)
            : pairWidth;
        if (digitWidth < 3) return new byte[0, 0];
        int digitStroke = Math.Min(stroke, (digitWidth - 1) / 2);
        int firstWidth = hundred ? stroke : digitWidth;
        int total = firstWidth + (text.Length - 1) * (gap + digitWidth);
        if (total > available) return new byte[0, 0];
        // 奇数列の余りは最初の字間へ。高さで字幅が決まった段でも左右をそろえる。
        int extra = (width - total) % 2;
        int left = (width - total - extra) / 2;
        var ink = new byte[inkHeight, width];
        for (int i = 0; i < text.Length; i++)
        {
            int slot = i == 0 ? firstWidth : digitWidth;
            int thickness = hundred && i == 0 ? stroke : digitStroke;
            // 単独の「1」は線幅の偶奇も合わせ、アイコンの中心を正確に通す。
            if (value == 1) thickness += (width - thickness) % 2;
            var glyph = InkBounds(text[i], width, left, slot, thickness);
            DrawGlyph(ink, text[i], glyph.Left, glyph.Width, thickness);
            left += slot + gap + (i == 0 ? extra : 0);
        }
        return ink;
    }

    private static (int Width, int Gap) FitPair(int available, int heightLimit, int stroke)
    {
        int widthLimit = (available - stroke) / 2;
        int slot = Math.Min(heightLimit, widthLimit);
        // 単独の字を左右均等に置ける最寄りの幅を、1 桁と 2 桁で共用する。
        if ((available - slot) % 2 != 0)
        {
            bool roundUp = slot < heightLimit && available - 2 * slot > stroke &&
                available - 2 * (slot + 1) >= 1;
            slot += roundUp ? 1 : -1;
        }
        // 高さ 6 の段では、穴と左右対称を両立する偶数の最小幅が 4。
        slot = Math.Max(slot, 3 + (available % 2 == 0 ? 1 : 0));
        int gap = widthLimit <= heightLimit ? available - 2 * slot : stroke;
        return (slot, Math.Max(1, gap));
    }

    // 負号も含めて必要な桁数で割り直し、点灯部分を中央に置く。
    private static byte[,] DrawFittedNumber(string text, int width, int height, int pairWidth, int stroke)
    {
        int available = width - 2, count = text.Length, gaps = count - 1;
        // まず字間を詰める。3 列の通常字でも一杯なら、負号のみ短くできる。
        int gap = Math.Clamp((available - 3 * count) / gaps, 1, stroke);
        int digitWidth = Math.Min(pairWidth, (available - gaps * gap) / count);
        int firstWidth = digitWidth;
        if (digitWidth < 3)
        {
            digitWidth = 3;
            firstWidth = text[0] == '-' ? Math.Clamp(available - gaps * (3 + gap), 1, 3) : 3;
        }
        int free = available - firstWidth - gaps * digitWidth;
        if (free < 0) return new byte[0, 0];
        int spacing = Math.Min(free, gaps * gap);
        int left = (width - firstWidth - gaps * digitWidth - spacing) / 2;
        int thickness = Math.Min(stroke, (digitWidth - 1) / 2);
        // いったん等幅の桁枠に置き、両端の「1」が枠の内側にある分も含めて中央を求める。
        Span<int> inkLeft = stackalloc int[count];
        Span<int> inkWidth = stackalloc int[count];
        for (int i = 0; i < count; i++)
        {
            int slot = i == 0 ? firstWidth : digitWidth;
            (inkLeft[i], inkWidth[i]) = InkBounds(text[i], width, left, slot, thickness);
            left += slot + ((i + 1) * spacing / gaps - i * spacing / gaps);
        }
        int span = inkLeft[^1] + inkWidth[^1] - inkLeft[0];
        int extra = (width - span) % 2;
        int shift = (width - span - extra) / 2 - inkLeft[0];
        var ink = new byte[height, width];
        for (int i = 0; i < count; i++)
            DrawGlyph(ink, text[i], inkLeft[i] + shift + (i > 0 ? extra : 0), inkWidth[i], thickness);
        return ink;
    }

    // 半ドットの端数はアイコンの中心側へ寄せる。「11」の左右が鏡写しになる。
    private static (int Left, int Width) InkBounds(char c, int frame, int left, int slot, int stroke)
    {
        if (c != '1') return (left, slot);
        int round = 2 * left + slot < frame ? 1 : 0;
        return (left + (slot - stroke + round) / 2, stroke);
    }

    private static void DrawGlyph(byte[,] ink, char c, int left, int width, int stroke)
    {
        byte lit = segments[c == '-' ? 10 : c - '0'];
        int height = ink.GetLength(0);
        int middle = (height - stroke) / 2;
        // 上下の縦棒が両方点灯したときは中央で接し、1 は一本の線、0 は閉じた輪になる。
        int split = height / 2;
        int right = left + width - stroke;

        Bar(A, left, 0, width, stroke);
        Bar(B, right, 0, stroke, split);
        Bar(C, right, split, stroke, height - split);
        Bar(D, left, height - stroke, width, stroke);
        Bar(E, left, split, stroke, height - split);
        Bar(F, left, 0, stroke, split);
        Bar(G, left, middle, width, stroke);

        void Bar(byte segment, int x, int y, int w, int h)
        {
            if ((lit & segment) == 0) return;
            for (int row = y; row < y + h; row++)
                for (int col = x; col < x + w; col++)
                    ink[row, col] = byte.MaxValue;
        }
    }
}
