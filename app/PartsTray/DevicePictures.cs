// 電池アイコンの内側に機器ごとの絵を描きます。
// 形を変えるときは対応する種類のメソッドを編集します。
using PartsTray.Core;

namespace PartsTray;

internal static class DevicePictures
{
    // 機器の絵を自分で描く（11/20 ユーザー：マウスの真ん中の線を残す、ヘッドホンの耳当てを丸く）。穴は透明で抜く。
    public static void Draw(Graphics g, BatteryDeviceKind kind, RectangleF a, Color color, int size)
    {
        using var brush = new SolidBrush(color);
        using var clear = new SolidBrush(Color.Transparent);
        var mode = g.CompositingMode;
        switch (kind)
        {
            case BatteryDeviceKind.Mouse: Mouse(g, a, color, brush, clear); break;
            case BatteryDeviceKind.Keyboard: Keyboard(g, a, color, brush, clear); break;
            case BatteryDeviceKind.Headphones: Headphones(g, a, color, brush, clear); break;
            case BatteryDeviceKind.Earbuds: Earbuds(g, a, brush, size); break;
            case BatteryDeviceKind.Gamepad: Gamepad(g, a, brush); break;
            default: Other(g, a, brush); break;
        }
        g.CompositingMode = mode;
    }
    private static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // 真ん中に境目がある縦長のマウス。
    private static void Mouse(Graphics g, RectangleF a, Color color, Brush brush, Brush clear)
    {
        float w = a.Width * 0.64f;
        using (var body = Rounded(new RectangleF(a.X + (a.Width - w) / 2, a.Y, w, a.Height), w / 2)) g.FillPath(brush, body);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        float line = Math.Max(1f, a.Width / 14f);   // 左右のボタンの境目（上から 4 割）
        g.FillRectangle(clear, a.X + a.Width / 2 - line / 2, a.Y, line, a.Height * 0.42f);
    }

    // 角の丸い横長のキーボード。キーの穴は縮小後に FinishKeyboard で抜く。
    private static void Keyboard(Graphics g, RectangleF a, Color color, Brush brush, Brush clear)
    {
        float h = a.Height * 0.66f; var body = new RectangleF(a.X, a.Y + (a.Height - h) / 2, a.Width, h);
        using (var path = Rounded(body, a.Width * 0.14f)) g.FillPath(brush, path);
    }

    // 円に沿うヘッドバンドと丸い耳当て。
    private static void Headphones(Graphics g, RectangleF a, Color color, Brush brush, Brush clear)
    {
        // iPhone の記号のように、ヘッドバンドと耳当ての外側を 1 つの円に沿わせる（11/20 ユーザー：耳当てが輪にかかって見える）。
        // ヘッドバンドは円の上半分から両脇の少し下まで。耳当ては、円の中に収まるよう円で切り取った縦長の形。
        // 見本で 5 通り比べて決めた値：線は絵の幅の 1/5、耳当ての幅は 0.40、絵の高さの 44% の所から下まで。
        float pen = Math.Max(1.5f, a.Width / 5f);
        var circle = new RectangleF(a.X + pen / 2, a.Y + pen / 2, a.Width - pen, a.Height - pen);
        using (var band = new Pen(color, pen)) g.DrawArc(band, circle, 150, 240);
        float cupWidth = a.Width * 0.40f, cupTop = a.Y + a.Height * 0.44f, cupBottom = a.Bottom;
        using var disk = new System.Drawing.Drawing2D.GraphicsPath();
        disk.AddEllipse(a);
        var clip = g.Clip;
        g.SetClip(disk);
        // 円で切るので外側は円に沿って曲がる。内側の角だけ丸くする。
        using (var left = Rounded(new RectangleF(a.X - cupWidth, cupTop, cupWidth * 2, cupBottom - cupTop), cupWidth * 0.5f)) g.FillPath(brush, left);
        using (var right = Rounded(new RectangleF(a.Right - cupWidth, cupTop, cupWidth * 2, cupBottom - cupTop), cupWidth * 0.5f)) g.FillPath(brush, right);
        g.Clip = clip;
    }

    // 左右対称の軸付きイヤホン。真ん中の空白は縮小後にも 2 列を透明にする。
    private static void Earbuds(Graphics g, RectangleF a, Brush brush, int size)
    {
        const int k = 4;
        float W = a.Width, H = a.Height, headW = W * 0.46f, headH = H * 0.40f, stemW = W * 0.19f, hy = a.Y + H * 0.02f;
        int center = size / 2;
        float leftInner = (center - 1) * k, rightInner = (center + 1) * k;
        float top = hy + headH * 0.45f;
        g.FillEllipse(brush, leftInner - headW, hy, headW, headH);
        using (var stem = Rounded(new RectangleF(leftInner - stemW, top, stemW, a.Bottom - top), stemW / 2)) g.FillPath(brush, stem);
        g.FillEllipse(brush, rightInner, hy, headW, headH);
        using (var stem = Rounded(new RectangleF(rightInner, top, stemW, a.Bottom - top), stemW / 2)) g.FillPath(brush, stem);
    }

    // 横長の本体と左右の握り。スティックと四つのボタンは縮小後に穴を抜く。
    private static void Gamepad(Graphics g, RectangleF a, Brush brush)
    {
        float W = a.Width, H = a.Height;
        using (var body = Rounded(new RectangleF(a.X, a.Y + H * 0.18f, W, H * 0.50f), H * 0.22f)) g.FillPath(brush, body);
        using (var left = Rounded(new RectangleF(a.X, a.Y + H * 0.30f, W * 0.38f, H * 0.62f), W * 0.19f)) g.FillPath(brush, left);
        using (var right = Rounded(new RectangleF(a.Right - W * 0.38f, a.Y + H * 0.30f, W * 0.38f, H * 0.62f), W * 0.19f)) g.FillPath(brush, right);
    }

    // 右に小さな端子が出た、塗りつぶしの横向き電池。
    private static void Other(Graphics g, RectangleF a, Brush brush)
    {
        float W = a.Width, H = a.Height;
        using (var body = Rounded(new RectangleF(a.X, a.Y + H * 0.25f, W * 0.86f, H * 0.50f), H * 0.10f)) g.FillPath(brush, body);
        g.FillRectangle(brush, a.X + W * 0.88f, a.Y + H * 0.40f, W * 0.12f, H * 0.20f);
    }

    // 11/20 の指定どおり、縮小済みの絵だけを仕上げる（輪には触れない）。
    public static void Finish(Bitmap picture, BatteryDeviceKind kind)
    {
        if (kind == BatteryDeviceKind.Earbuds) FinishEarbuds(picture);
        else if (kind == BatteryDeviceKind.Gamepad) FinishGamepad(picture);
        else if (kind == BatteryDeviceKind.Keyboard) FinishKeyboard(picture);
    }

    // 左右の隙間 2 列は透明、隣の 2 列だけは不透明度を 0/1 にする。
    private static void FinishEarbuds(Bitmap picture)
    {
        int center = picture.Width / 2;
        for (int y = 0; y < picture.Height; y++)
        {
            // 要確認: 指定の左右対称座標でも GDI+ の縮小で alpha に最大 15/255 の差が出た。
            // 左右対称という指定を画素でも満たすため、滑らかな左半分を右へ鏡映する（形の座標は指定どおり）。
            for (int x = 0; x < center; x++) picture.SetPixel(picture.Width - 1 - x, y, picture.GetPixel(x, y));
            picture.SetPixel(center - 1, y, Color.Transparent); picture.SetPixel(center, y, Color.Transparent);
            foreach (int x in new[] { center - 2, center + 1 })
            {
                var c = picture.GetPixel(x, y);
                picture.SetPixel(x, y, c.A >= 128 ? Color.FromArgb(255, c) : Color.Transparent);
            }
        }
    }

    // 左は四角いスティック、右はひし形の四頂点のボタンを透明に抜く。
    private static void FinishGamepad(Bitmap picture)
    {
        int size = picture.Width;
        float box = size - 2 * (size / 10f + size / 10f), bx = (size - box) / 2;
        int cy = (int)Math.Round(bx + box * 0.42f), arm = Math.Max(1, (int)Math.Round(box / 12f));
        int dx = (int)Math.Round(bx + box * 0.25f), ax = (int)Math.Round(bx + box * 0.72f);
        void Hole(int x, int y) { if (x >= 0 && x < size && y >= 0 && y < size) picture.SetPixel(x, y, Color.Transparent); }
        for (int y = 0; y < 2 * arm; y++) for (int x = 0; x < 2 * arm; x++) Hole(dx - arm + x, cy - arm + 1 + y);
        Hole(ax, cy - arm); Hole(ax + arm, cy); Hole(ax, cy + arm); Hole(ax - arm, cy);
    }

    // 粒を 2 段に並べ、2 段目を 1 ドットずらし、その下に長さ 6 のスペースキーを抜く。
    private static void FinishKeyboard(Bitmap picture)
    {
        int size = picture.Width, top = -1, bottom = -1, left = -1, right = -1;
        for (int y = 0; y < size; y++) if (picture.GetPixel(size / 2, y).A > 0) { if (top < 0) top = y; bottom = y; }
        if (top < 0) return;
        for (int x = 0; x < size; x++) if (picture.GetPixel(x, (top + bottom) / 2).A > 0) { if (left < 0) left = x; right = x; }
        void Hole(int x, int y) { if (left < x && x < right && top < y && y < bottom) picture.SetPixel(x, y, Color.Transparent); }
        int y0 = top + 2;
        for (int x = left + 1; x < right; x += 2) Hole(x, y0);
        for (int x = left + 2; x < right; x += 2) Hole(x, y0 + 2);
        int sy = y0 + 4, sx = size / 2 - 3;
        for (int x = sx; x < sx + 6; x++) Hole(x, sy);
    }
}
