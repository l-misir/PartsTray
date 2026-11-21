// 数字「0」の外枠を保ち、離れた点と内側の穴を二つに分ける斜線を消します。
namespace PartsTray.Core;

public static class PlainZero
{
    private const int Ink = -1, Outline = -2, Exterior = 1;

    public static bool[,] Clean(bool[,] glyph) => CleanGlyph(glyph);
    public static byte[,] Clean(byte[,] glyph) => CleanGlyph(glyph);

    private static T[,] CleanGlyph<T>(T[,] glyph) where T : struct, IEquatable<T>
    {
        int height = glyph.GetLength(0), width = glyph.GetLength(1);
        // インクが画像の端に届いていても、周囲の背景が一続きになるようにする。
        var areas = new int[height + 2, width + 2];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (!glyph[y, x].Equals(default)) areas[y + 1, x + 1] = Ink;

        var queue = new Queue<(int Y, int X)>();
        Fill(0, 0, 0, Exterior);
        int lastHole = Exterior;
        for (int y = 1; y <= height; y++)
            for (int x = 1; x <= width; x++)
            {
                if (areas[y, x] == 0) Fill(y, x, 0, ++lastHole);
                else if (areas[y, x] == Ink && TouchesExterior(y, x)) Fill(y, x, Ink, Outline);
            }
        if (lastHole == Exterior) return glyph;

        T[,]? clean = null;
        for (int y = 1; y <= height; y++)
            for (int x = 1; x <= width; x++)
            {
                // 外枠につながらないインクは点。Terminus の斜線は両端が枠と
                // つながるので、別々の穴にはさまれたインクの部分だけを消す。
                bool slash = lastHole > Exterior + 1 && areas[y, x] == Outline && !TouchesExterior(y, x)
                    && (BetweenHoles(y, x, 1, 0) || BetweenHoles(y, x, 0, 1));
                if (areas[y, x] != Ink && !slash) continue;
                clean ??= (T[,])glyph.Clone();
                clean[y - 1, x - 1] = default;
            }
        return clean ?? glyph;

        bool TouchesExterior(int y, int x) => areas[y - 1, x] == Exterior || areas[y + 1, x] == Exterior
            || areas[y, x - 1] == Exterior || areas[y, x + 1] == Exterior;

        bool BetweenHoles(int y, int x, int dy, int dx)
        {
            int firstY = y, firstX = x, lastY = y, lastX = x;
            do { firstY -= dy; firstX -= dx; } while (areas[firstY, firstX] == Outline);
            do { lastY += dy; lastX += dx; } while (areas[lastY, lastX] == Outline);
            int first = areas[firstY, firstX], last = areas[lastY, lastX];
            return first > Exterior && last > Exterior && first != last;
        }

        // 背景は上下左右にたどり、斜め1ドットでつながる「0」の外枠も残す。
        void Fill(int y, int x, int from, int to)
        {
            areas[y, x] = to;
            queue.Enqueue((y, x));
            while (queue.TryDequeue(out var cell))
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (from != Ink && dy != 0 && dx != 0) continue;
                        int nextY = cell.Y + dy, nextX = cell.X + dx;
                        if (nextY < 0 || nextY > height + 1 || nextX < 0 || nextX > width + 1) continue;
                        if (areas[nextY, nextX] != from) continue;
                        areas[nextY, nextX] = to;
                        queue.Enqueue((nextY, nextX));
                    }
        }
    }
}
