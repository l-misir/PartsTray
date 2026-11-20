// オウムの GIF・PNG の読み込みと縮小を担当します。
// ふちの処理やコマの切り取り・拡大縮小を変えるときはここを編集します。
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal static class ParrotFrames
{
    public static (Bitmap[] Frames, bool Colored) Load(ILog log, bool light = false)
    {
        // 11/19 ユーザーが用意した本家のパーティーパロット（10 コマ、体の色が虹色に変わる）を優先して使う。
        string gif = Path.Combine(AppContext.BaseDirectory, "Assets", "parrot", "partyparrot.gif");
        if (File.Exists(gif))
        {
            try { return (LoadGif(gif, light), true); }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or OutOfMemoryException or UnauthorizedAccessException)
            { log.Write($"partyparrot.gif を読めないため、RunCat365 の絵を使います: {ex.Message}"); }
        }
        return (LoadPngFrames(log), false);
    }

    private static Bitmap[] LoadGif(string path, bool light)
    {
        using var image = Image.FromFile(path);
        var dimension = new FrameDimension(image.FrameDimensionsList[0]);
        int count = image.GetFrameCount(dimension);
        var loaded = new List<Bitmap>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                image.SelectActiveFrame(dimension, i);
                var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImage(image, 0, 0, image.Width, image.Height);
                // タスクバーが黒なので、黒いふちと目は透明にする。黒い背景の上では見た目が同じで、
                // 縮めたときにふちの黒が体の色に混ざって暗く見えるのを防げる。
                if (!light) EditPixels(bitmap, pixels =>
                {
                    for (int k = 0; k < pixels.Length; k += 4)   // BGRA
                        if (pixels[k + 3] > 0 && pixels[k] < 64 && pixels[k + 1] < 64 && pixels[k + 2] < 64)
                            pixels[k] = pixels[k + 1] = pixels[k + 2] = pixels[k + 3] = 0;
                });
                loaded.Add(bitmap);
            }
            if (loaded.Count == 0) throw new ArgumentException("コマがありません");
            var bounds = ContentBounds(loaded);
            if (!bounds.IsEmpty && bounds != new Rectangle(Point.Empty, loaded[0].Size))
                for (int i = 0; i < loaded.Count; i++)
                {
                    var cropped = loaded[i].Clone(bounds, PixelFormat.Format32bppArgb);
                    loaded[i].Dispose(); loaded[i] = cropped;
                }
            return loaded.ToArray();
        }
        catch { foreach (var bitmap in loaded) bitmap.Dispose(); throw; }
    }

    private static Bitmap[] LoadPngFrames(ILog log)
    {
        var loaded = new List<Bitmap>();
        try
        {
            for (int i = 0; i < 10; i++)
            {
                using var source = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "parrot", $"parrot_{i}.png"));
                loaded.Add(new Bitmap(source));
            }
            // RunCat365 の絵は 32×32 の右下に寄っていて、上に余白がある。
            // 全コマの絵がある範囲（和集合）で切り取り、小さなアイコンでも絵を大きく描く。コマ間の位置関係は保つ。
            var bounds = ContentBounds(loaded);
            if (!bounds.IsEmpty && bounds != new Rectangle(Point.Empty, loaded[0].Size))
                for (int i = 0; i < loaded.Count; i++)
                {
                    var cropped = loaded[i].Clone(bounds, PixelFormat.Format32bppArgb);
                    loaded[i].Dispose(); loaded[i] = cropped;
                }
            return loaded.ToArray();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or OutOfMemoryException or UnauthorizedAccessException)
        {
            foreach (var bitmap in loaded) bitmap.Dispose();
            log.Write("オウムの絵が見つからないため、代わりの丸を表示します");
            var fallback = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(fallback);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillEllipse(Brushes.White, 1, 1, 30, 30);
            return [fallback];
        }
    }

    // 32 ビット ARGB の画素（BGRA の並び）を、まとめて読み書きする（GetPixel は遅いため）。
    private static void EditPixels(Bitmap bitmap, Action<byte[]> edit)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride != bitmap.Width * 4) throw new ArgumentException("想定外の画素の並び");
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            edit(pixels);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static Rectangle ContentBounds(List<Bitmap> bitmaps)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        foreach (var bitmap in bitmaps)
            EditPixels(bitmap, pixels =>
            {
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        if (pixels[(y * bitmap.Width + x) * 4 + 3] > 16)
                        {
                            left = Math.Min(left, x); top = Math.Min(top, y);
                            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                        }
            });
        if (right < 0) return Rectangle.Empty;
        // 全コマが同じ大きさでなければ、切り取らない。
        if (bitmaps.Any(b => b.Size != bitmaps[0].Size)) return new Rectangle(Point.Empty, bitmaps[0].Size);
        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }


    public static Bitmap[] Scale(Bitmap[] frames, int size, int imageHeight, bool stretch, int widen)
    {
        var result = new Bitmap[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            var source = frames[i];
            double scale = Math.Min((double)size / source.Width, (double)imageHeight / source.Height);
            // stretch：縦だけを枠の高さまで伸ばす（縮めることはしない。数字を添えて枠が低いときは縦横比のまま）。
            double scaleY = stretch ? Math.Max(scale, (double)imageHeight / source.Height) : scale;
            // widen：横を左右それぞれ widen ドット伸ばす。アイコンより広くなった分は、DrawParrot で真ん中に置いたときに両端が切れる。
            int width = Math.Max(1, (int)Math.Round(source.Width * scale) + widen * 2), height = Math.Max(1, (int)Math.Round(source.Height * scaleY));
            var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using var wrap = new ImageAttributes();
                wrap.SetWrapMode(WrapMode.TileFlipXY);   // ふちの外側が黒くにじまないように
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrap);
            }
            result[i] = scaled;
        }
        return result;
    }
}
