// 数字を指定の描き方で描き、各ドットの濃さをアルファ値として取り出します。
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal static partial class DigitArt
{
    internal static byte[,] Rasterize(string text, Font font, int width, int height, int origin,
        DigitRendering? rendering, bool plainZero)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        (int Left, int Right)[] zeroColumns = [];
        using (var graphics = Graphics.FromImage(bitmap))
        using (var format = StringFormat.GenericTypographic)
        {
            graphics.TextRenderingHint = rendering == DigitRendering.Smooth
                ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.SingleBitPerPixelGridFit;
            graphics.DrawString(text, font, Brushes.White, origin, origin, format);
            if (plainZero && text.Length > 1 && text.Contains('0'))
                zeroColumns = MeasureZeroColumns(graphics, text, font, format, width, height, origin);
        }

        var mask = ReadAlpha(bitmap);
        if (plainZero && text == "0") return PlainZero.Clean(mask);
        foreach (var (left, right) in zeroColumns)
            CleanZeroColumns(mask, left, right);
        return mask;
    }

    // 数字をまとめて描く従来の配置も、描画時の文字送りで 0 ごとの範囲を求める。
    private static (int Left, int Right)[] MeasureZeroColumns(Graphics graphics, string text, Font font,
        StringFormat format, int width, int height, int origin)
    {
        var characters = Enumerable.Range(0, text.Length).Where(i => text[i] == '0')
            .Select(i => new CharacterRange(i, 1)).ToArray();
        format.SetMeasurableCharacterRanges(characters);
        format.FormatFlags |= StringFormatFlags.NoWrap;
        var regions = graphics.MeasureCharacterRanges(text, font, new RectangleF(origin, origin, width, height), format);
        try
        {
            var columns = new (int Left, int Right)[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                var bounds = regions[i].GetBounds(graphics);
                columns[i] = (Math.Clamp((int)Math.Floor(bounds.Left), 0, width),
                    Math.Clamp((int)Math.Ceiling(bounds.Right), 0, width));
            }
            return columns;
        }
        finally
        {
            foreach (var region in regions) region.Dispose();
        }
    }

    private static void CleanZeroColumns(byte[,] mask, int left, int right)
    {
        int width = mask.GetLength(1), height = mask.GetLength(0), span = right - left;
        if (span <= 0) return;
        var glyph = new byte[height, span];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(mask, y * width + left, glyph, y * span, span);
        var cleaned = PlainZero.Clean(glyph);
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(cleaned, y * span, mask, y * width + left, span);
    }

    private static byte[,] ReadAlpha(Bitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var mask = new byte[height, width];
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            var row = new byte[4 * width];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < width; x++)
                    mask[y, x] = row[4 * x + 3];
            }
        }
        finally { bitmap.UnlockBits(data); }
        return mask;
    }

    // 指定の段の中央に数字を載せる。想定外の大きさでも枠の外へは書き込まない。
    internal static void DrawMask(Bitmap bitmap, byte[,] glyphs, int top, Color color)
    {
        int width = glyphs.GetLength(1), height = glyphs.GetLength(0);
        if (width == 0 || height == 0) return;
        using var ink = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int alpha = glyphs[y, x];
                if (alpha > 0) ink.SetPixel(x, y, Color.FromArgb(alpha * color.A / 255, color));
            }
        // 通知領域の透明背景・塗りつぶし・オウム画像のどれにも同じアルファで重ねる。
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImageUnscaled(ink, (bitmap.Width - width) / 2, top);
    }
}
