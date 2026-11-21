// モニターの数字とオウムをアイコンへ描きます。
// 配置や数字の色を変えるときはここを編集します。
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class MonitorModule
{
    private static Bitmap Canvas(int size) => new(size, size, PixelFormat.Format32bppArgb);
    private Color ParseColor(string text, Color fallback, string background = "")
    {
        Color color = MonitorColor.TryParse(text, out var c) ? Color.FromArgb(c.R, c.G, c.B) : fallback;
        return MonitorColor.TryParse(background, out var b)
            ? Palette.ForBackground(color, Color.FromArgb(b.R, b.G, b.B))
            : Palette.ForTaskbar(color, theme.IsLight);
    }

    private static void FillBackground(Bitmap bitmap, string text, int top, int height)
    {
        if (!MonitorColor.TryParse(text, out var c)) return;
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
        graphics.FillRectangle(brush, 0, top, bitmap.Width, height);
    }

    private void DrawTempMemory()
    {
        if (TrayInteraction.Busy) return;   // 通知領域で並べ替え中は描き替えない
        if (settings.TempMemory.Split)
        {
            DrawBig(mainSlot, temperature is { } reading ? MonitorMath.Round(reading.Average) : null, ParseColor(settings.TempMemory.TempColor, Color.Red, settings.TempMemory.TempBackgroundColor), settings.TempMemory.TempBackgroundColor);
            return;
        }
        int size = IconSize(), wide = MonitorFont.TempMemoryHeight(size), height = DigitArt.Height(size, settings.DigitStyle, wide), row = (size - 1) / 2;
        using var bitmap = Canvas(size);
        FillBackground(bitmap, settings.TempMemory.TempBackgroundColor, 0, size / 2);
        FillBackground(bitmap, settings.TempMemory.MemoryBackgroundColor, size / 2, size - size / 2);
        string tempText = temperature is { } t ? DigitArt.Fit(MonitorMath.Round(t.Average), size, settings.DigitStyle, wide) : "--";
        DrawText(bitmap, tempText, height, (row - height) / 2, ParseColor(settings.TempMemory.TempColor, Color.Red, settings.TempMemory.TempBackgroundColor));
        string memoryText = memoryLoad is { } load ? DigitArt.Fit((int)load, size, settings.DigitStyle, wide) : "--";
        DrawText(bitmap, memoryText, height, size - row + (row - height) / 2, ParseColor(settings.TempMemory.MemoryColor, Color.White, settings.TempMemory.MemoryBackgroundColor));
        mainSlot.Replace(bitmap);
    }

    private void DrawBig(TrayIconSlot<BigDrawingKey> slot, int? value, Color color, string background)
    {
        if (TrayInteraction.Busy) return;
        int size = IconSize();
        var key = new BigDrawingKey(value, size, settings.DigitStyle, color.ToArgb(), background);
        if (slot.Key == key) return;
        var mask = DigitArt.Big(value, size, settings.DigitStyle);
        // 例: 幅不足で 100 も 99 と描く場合、実値だけが変わっても差し替えない。
        if (slot.Key is { } previous && previous with { Value = value } == key && slot.BigMask is { } old &&
            old.GetLength(0) == mask.GetLength(0) && old.GetLength(1) == mask.GetLength(1) &&
            (ReferenceEquals(old, mask) || old.Cast<bool>().SequenceEqual(mask.Cast<bool>())))
        {
            slot.Key = key;
            return;
        }
        using var bitmap = Canvas(size);
        FillBackground(bitmap, background, 0, size);
        int left = (size - mask.GetLength(1)) / 2, top = (size - mask.GetLength(0)) / 2;
        // 万一アイコンより大きい文字が来ても落ちないよう、はみ出した点は描かない（起動時に例外が出るとアプリごと止まる）。
        for (int y = 0; y < mask.GetLength(0); y++)
            for (int x = 0; x < mask.GetLength(1); x++)
                if (mask[y, x] && left + x >= 0 && left + x < size && top + y >= 0 && top + y < size)
                    bitmap.SetPixel(left + x, top + y, color);
        if (TrayInteraction.Busy) return;
        slot.Replace(bitmap);
        slot.Key = key; slot.BigMask = mask;
    }

    private void DrawParrot()
    {
        if (!ParrotEnabled || TrayInteraction.Busy) return;   // 通知領域で並べ替え中は描き替えない
        ReadTheme();
        int size = IconSize();
        int textHeight = settings.Parrot.CpuText == "none" ? 0 : DigitArt.Height(size, settings.DigitStyle, MonitorFont.ParrotHeight(size));
        // 絵と数字の間に 1 ドット空ける。
        int imageHeight = textHeight == 0 ? size : size - textHeight - 1;
        int imageTop = settings.Parrot.CpuText == "top" ? textHeight + 1 : 0;
        var scaled = ScaledFrames(size, imageHeight);
        var source = scaled[frame % scaled.Length];
        using var bitmap = Canvas(size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var target = new Rectangle((size - source.Width) / 2, imageTop + (imageHeight - source.Height) / 2, source.Width, source.Height);
            if (coloredFrames) graphics.DrawImageUnscaled(source, target);
            else
            {
                Color color = foreground;
                if (settings.Parrot.Rainbow)
                {
                    var (r, g, b) = MonitorMath.Rainbow(frame, elapsed.ElapsedMilliseconds);
                    color = Color.FromArgb(r, g, b);
                }
                using var attributes = new ImageAttributes();
                // RGB を一定色に置き換え、元画像の alpha だけを引き継ぐ。
                attributes.SetColorMatrix(new ColorMatrix(new[]
                {
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 1, 0 },
                    new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 0, 1 }
                }));
                graphics.DrawImage(source, target, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
        }
        if (textHeight != 0)
            DrawText(bitmap, DigitArt.Fit(MonitorMath.Round(cpu), size, settings.DigitStyle, MonitorFont.ParrotHeight(size)), textHeight,
                settings.Parrot.CpuText == "top" ? 0 : size - textHeight, ParseColor(settings.Parrot.CpuTextColor, Color.White));
        parrotSlot.Replace(bitmap);
        SetTooltip(parrot, $"CPU {MonitorMath.Round(cpu)}%");
    }

    // アイコンの大きさに合わせて縮めたコマ。大きさが変わったときだけ作り直す（毎コマ縮めると重いため）。
    private Bitmap[] ScaledFrames(int size, int imageHeight)
    {
        bool stretch = settings.Parrot.Stretch;
        int widen = settings.Parrot.WidenPx;
        if (scaledFrames != null && scaledKey == (size, imageHeight, stretch, widen)) return scaledFrames;
        if (scaledFrames != null) foreach (var old in scaledFrames) old.Dispose();
        scaledKey = (size, imageHeight, stretch, widen);
        return scaledFrames = ParrotFrames.Scale(frames, size, imageHeight, stretch, widen);
    }

    // 数字は点の文字のまま、横にだけ伸ばして幅いっぱいに描く（11/19 ユーザーの要望）。
    private void DrawText(Bitmap bitmap, string text, int height, int top, Color color)
    {
        var glyphs = DigitArt.Mask(text, bitmap.Width, settings.DigitStyle, height);
        int left = (bitmap.Width - glyphs.GetLength(1)) / 2;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < glyphs.GetLength(1); x++)
                if (glyphs[y, x]) bitmap.SetPixel(left + x, top + y, color);
    }

}
