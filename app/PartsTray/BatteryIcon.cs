// 電池の輪・稲妻・縮小・切断時の薄い表示を描きます。
// 線の太さや印の位置を変えるときはここを編集します。
using System.Drawing.Imaging;
using PartsTray.Core;

namespace PartsTray;

internal static class BatteryIcon
{
    // 輪と絵を別々に 4 倍の大きさで描いてから縮め、輪の上に絵を重ねる（Windows の記号と同じく縁が滑らかになる）。
    // 輪：どの機器も滑らか（11/20 ユーザーの指定。見本 5 の「ぼかさない縮め方」も試したうえで見本 3 に決定）。
    //   縁が半分透けて黒い背景と混ざり、色がくすんで見えるので、輪の色を明るくする（緑は色合いを変えずに一番明るく）。
    // 絵：crispPicture（マウス・キーボード）なら縮めた後で点を 0/1 に分けてくっきりさせる。ヘッドホンは滑らかのまま。
    // rainbowHue を渡すと、輪を虹色の一周にする（残量 100% のとき。値は虹の始まりの色の角度で、回すときはこれをずらす）。
    public static Bitmap RingIcon(BatteryDeviceKind kind, int? percent, Color color, int size, bool crispPicture, bool light = false, double? rainbowHue = null)
    {
        const int k = 4; int s = size * k;
        Bitmap icon;
        using (var ringBig = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(ringBig))
            {
                if (rainbowHue is { } hue) DrawRainbow(g, s, hue, light);
                else DrawRing(g, s, percent, Palette.ForTaskbar(light ? color : Palette.Brighten(color), light));
            }
            icon = Shrink(ringBig, size);
        }
        using var pictureBig = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(pictureBig)) DrawPicture(g, s, kind, Palette.ForTaskbar(Palette.White, light));
        using var picture = Shrink(pictureBig, size);
        if (crispPicture)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = picture.GetPixel(x, y);
                    picture.SetPixel(x, y, c.A >= 128 ? Color.FromArgb(255, c) : Color.Transparent);
                }
        DevicePictures.Finish(picture, kind);
        using (var g = Graphics.FromImage(icon))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.DrawImageUnscaled(picture, 0, 0);
        }
        return icon;
    }
    // 20px のアイコンで太さ 2 ドットの輪（s は描く大きさ）。
    // 11/20 ユーザーの指定（見本 B）：輪をアイコンの端まで 1 ドット広げる。くっきりにしたときの見栄えがよくなる。
    // 11/20 ユーザーの指定：減った部分（灰色の輪）は描かない。数字を出さない時点で正確さより見栄えを優先する。
    private static void DrawRing(Graphics g, int s, int? percent, Color color)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        float thick = s / 10f, inset = thick / 2f;
        var ring = new RectangleF(inset, inset, s - inset * 2, s - inset * 2);
        if (percent is int p && p > 0)
            using (var arc = new Pen(color, thick) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawArc(arc, ring, -90, 360f * Math.Clamp(p, 0, 100) / 100f);
    }
    // 虹色の輪（11/20 ユーザーが見本 B を選んだ）：上から時計回りに、一周で虹を 1 周。hue を増やすと帯が回って見える。
    // 細かい弧を 90 本つないで描く（GDI+ には円に沿う色の変化の筆がないため）。継ぎ目が透けないよう、少し重ねる。
    private static void DrawRainbow(Graphics g, int s, double hue, bool light)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        float thick = s / 10f, inset = thick / 2f;
        var ring = new RectangleF(inset, inset, s - inset * 2, s - inset * 2);
        const int steps = 90;
        for (int i = 0; i < steps; i++)
        {
            using var pen = new Pen(Palette.ForTaskbar(Palette.Rainbow(hue + i * 360.0 / steps), light), thick);
            g.DrawArc(pen, ring, -90 + i * 360f / steps, 360f / steps + 1.2f);
        }
    }
    // 輪の内側に白い機器の絵（s は描く大きさ）。
    private static void DrawPicture(Graphics g, int s, BatteryDeviceKind kind, Color color)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        float thick = s / 10f, inner = s - 2 * (thick + s / 10f);
        DevicePictures.Draw(g, kind, new RectangleF((s - inner) / 2, (s - inner) / 2, inner, inner), color, s / 4);
    }
    // 滑らかに縮める（少しぼける）。
    private static Bitmap Shrink(Bitmap big, int size)
    {
        var icon = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(icon);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(big, new Rectangle(0, 0, size, size));
        return icon;
    }
    // 充電中の印：右下に黄色の稲妻（11/20 ユーザーの指定。「充電は黄色や黄色っぽいオレンジのイメージ」）。
    // 稲妻は 4 倍で描いて縮め、点を 0/1 に分けてくっきりさせる。まわり 1 ドットは透明にして、輪や絵と離す。
    // 置き場所は 20px のアイコンで横 13.5〜20・縦 11〜20 の箱（大きさに合わせて伸び縮みする）。
    public static void StampBolt(Bitmap icon, int size, bool light = false)
    {
        Color boltColor = Palette.ForTaskbar(Palette.Bolt, light);
        const int k = 4; int s = size * k; float unit = size / 20f;
        var box = new RectangleF(13.5f * unit, 11f * unit, 6.5f * unit, 9f * unit);
        // 稲妻の形（箱の中の割合）：上の辺 → 右上の帯を左下へ → 真ん中で右へ張り出す → 下の先へ → 左の帯を戻る。
        var shape = new (float X, float Y)[] { (0.50f, 0f), (0.88f, 0f), (0.62f, 0.40f), (1.0f, 0.40f), (0.22f, 1f), (0.42f, 0.56f), (0.02f, 0.56f) };
        bool[,] mask = new bool[size, size];
        using (var big = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(big))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.FillPolygon(Brushes.White, shape.Select(p => new PointF((box.X + p.X * box.Width) * k, (box.Y + p.Y * box.Height) * k)).ToArray());
            }
            using var small = Shrink(big, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) mask[y, x] = small.GetPixel(x, y).A >= 110;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1 && !near; dx++)
                    {
                        int yy = y + dy, xx = x + dx;
                        near = yy >= 0 && yy < size && xx >= 0 && xx < size && mask[yy, xx];
                    }
                if (near) icon.SetPixel(x, y, mask[y, x] ? boltColor : Color.Transparent);
            }
    }
    // 切断中は全体を薄くする（不透明度を factor 倍）。
    public static void Fade(Bitmap bitmap, double factor)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.A > 0) bitmap.SetPixel(x, y, Color.FromArgb((int)(p.A * factor), p));
            }
    }
}
