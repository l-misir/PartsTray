// 自作の数字 PNG を 11 字に切り分け、読み込めない場合は Core Temp の点の絵を返します。
namespace PartsTray.Core;

public static class DotDigitSheet
{
    private const string Characters = "0123456789-";

    public static string[][][] Load(string path, Func<Stream, string[][]> decode, Action<string> log)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            stream.ReadExactly(signature);
            if (!signature.SequenceEqual((ReadOnlySpan<byte>)[137, 80, 78, 71, 13, 10, 26, 10]))
                throw new InvalidDataException("PNG 形式の画像を指定してください。");
            stream.Position = 0;
            return [decode(stream)];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log($"digits.png を読み込めないため Dots:CoreTemp を使います。{ex.Message}");
            return [DotDigitSets.CoreTemp96, DotDigitSets.CoreTemp120];
        }
    }

    public static string[][] Read(int width, int height, Func<int, int, byte> alpha)
    {
        const int separators = 10;
        if (height < 1 || width < Characters.Length + separators || (width - separators) % Characters.Length != 0)
            throw new InvalidDataException("画像の幅は「11 × 1 文字の枠幅 ＋ 10」にしてください。");
        int cellWidth = (width - separators) / Characters.Length;
        int top = height, bottom = -1;
        var bounds = new (int Left, int Right)[Characters.Length];
        for (int i = 0; i < Characters.Length; i++)
        {
            int start = i * (cellWidth + 1);
            int left = start + cellWidth, right = -1;
            for (int y = 0; y < height; y++)
            {
                if (i > 0 && alpha(start - 1, y) != 0)
                    throw new InvalidDataException($"文字「{Characters[i]}」の前に完全に透明な列が 1 列必要です。");
                for (int x = start; x < start + cellWidth; x++)
                {
                    if (alpha(x, y) != byte.MaxValue) continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            if (right < left) throw new InvalidDataException($"文字「{Characters[i]}」に不透明な点がありません。");
            bounds[i] = (left, right);
        }

        // 上下の位置を全字で共有し、短い横棒やベースラインを描いたとおりに保つ。
        var glyphs = new string[Characters.Length][];
        for (int i = 0; i < glyphs.Length; i++)
        {
            var (left, right) = bounds[i];
            var glyph = new string[bottom - top + 1];
            var row = new char[right - left + 1];
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                    row[x - left] = alpha(x, y) == byte.MaxValue ? '#' : '.';
                glyph[y - top] = new string(row);
            }
            glyphs[i] = glyph;
        }
        return glyphs;
    }
}
