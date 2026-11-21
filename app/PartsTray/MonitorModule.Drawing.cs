// モニターの数字とオウムをアイコンへ描きます。
// 配置や数字の色を変えるときはここを編集します。
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class MonitorModule
{
    private int? DisplayCpu => digitPreview.Display(MonitorMath.Round(cpu));
    private int? DisplayTemperature => digitPreview.Display(temperature is { } t ? MonitorMath.Round(t.Average) : null);
    private int? DisplayMemory => digitPreview.Display((int?)memoryLoad);

    private void DrawMonitor()
    {
        UpdateTooltip(); // 初めて表示する前に、新しいアイコンの説明文も設定する。
        if (TempEnabled) DrawTempMemory();
        if (CpuEnabled)
        {
            DrawBig(cpuSlot, DisplayCpu, ParseColor(settings.Cpu.Color, Palette.Cyan, settings.Cpu.BackgroundColor),
                settings.Cpu.BackgroundColor, settings.ResolveDigitFont(settings.CpuDigitFont));
            order.Want(cpuIcon, true);
        }
        if (MemoryEnabled)
        {
            DrawBig(memorySlot, DisplayMemory,
                ParseColor(settings.TempMemory.MemoryColor, Color.White, settings.TempMemory.MemoryBackgroundColor),
                settings.TempMemory.MemoryBackgroundColor, settings.ResolveDigitFont(settings.MemoryDigitFont));
            order.Want(memoryIcon, true);
        }
        if (ParrotEnabled)
        {
            DrawParrot();
            order.Want(parrot, true);
        }
    }

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
            DrawBig(mainSlot, DisplayTemperature, ParseColor(settings.TempMemory.TempColor, Color.Red, settings.TempMemory.TempBackgroundColor),
                settings.TempMemory.TempBackgroundColor, settings.ResolveDigitFont(settings.TemperatureDigitFont), maximumValue: 150);
            return;
        }
        int size = IconSize();
        using var bitmap = Canvas(size);
        FillBackground(bitmap, settings.TempMemory.TempBackgroundColor, 0, size / 2);
        FillBackground(bitmap, settings.TempMemory.MemoryBackgroundColor, size / 2, size - size / 2);
        var (temp, memory) = DigitArt.TempMemoryInk(settings, DisplayTemperature, DisplayMemory, size, fileDigits, digitPreview: digitPreview.Enabled);
        DigitArt.DrawMask(bitmap, temp, DigitArt.RowTop(size, temp.GetLength(0), false, settings.ResolveDigitFont(settings.TemperatureDigitFont)),
            ParseColor(settings.TempMemory.TempColor, Color.Red, settings.TempMemory.TempBackgroundColor));
        DigitArt.DrawMask(bitmap, memory, DigitArt.RowTop(size, memory.GetLength(0), true, settings.ResolveDigitFont(settings.MemoryDigitFont)),
            ParseColor(settings.TempMemory.MemoryColor, Color.White, settings.TempMemory.MemoryBackgroundColor));
        mainSlot.Replace(bitmap);
    }

    private void DrawBig(TrayIconSlot<BigDrawingKey> slot, int? value, Color color, string background,
        string digitFont, int maximumValue = 100)
    {
        if (TrayInteraction.Busy) return;
        int size = IconSize();
        var source = DigitFontSpec.DotSet(digitFont) == DotDigitKind.File ? fileDigits : null;
        var key = new BigDrawingKey(value, size, settings.DigitStyle, settings.HundredStyle, digitFont,
            settings.PlainZero, digitPreview.Enabled, color.ToArgb(), background, source);
        if (slot.Key == key) return;
        var mask = DigitArt.BigInk(value, size, settings.DigitStyle, settings.HundredStyle, maximumValue, digitFont,
            settings.PlainZero, source, digitPreview: digitPreview.Enabled);
        // 例: 幅不足で 100 も 99 と描く場合、実値だけが変わっても差し替えない。
        if (slot.Key is { } previous && previous with { Value = value } == key && slot.BigMask is { } old &&
            old.GetLength(0) == mask.GetLength(0) && old.GetLength(1) == mask.GetLength(1) &&
            (ReferenceEquals(old, mask) || old.Cast<byte>().SequenceEqual(mask.Cast<byte>())))
        {
            slot.Key = key;
            return;
        }
        using var bitmap = Canvas(size);
        FillBackground(bitmap, background, 0, size);
        DigitArt.DrawMask(bitmap, mask, (size - mask.GetLength(0)) / 2, color);
        if (TrayInteraction.Busy) return;
        slot.Replace(bitmap);
        slot.Key = key; slot.BigMask = mask;
    }

    private void DrawParrot()
    {
        if (!ParrotEnabled || TrayInteraction.Busy) return;   // 通知領域で並べ替え中は描き替えない
        ReadTheme();
        int size = IconSize();
        int? overlay = settings.Parrot.CpuText == "none" ? null : DisplayCpu;
        var cpuMask = overlay.HasValue ?
            DigitArt.RowInk(overlay, size, settings.DigitStyle, MonitorFont.ParrotHeight(size), settings.HundredStyle,
                digitFont: settings.ResolveDigitFont(settings.CpuDigitFont), plainZero: settings.PlainZero,
                fileDigits: fileDigits, digitPreview: digitPreview.Enabled) : null;
        int textHeight = cpuMask?.GetLength(0) ?? 0;
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
        if (cpuMask != null)
        {
            DigitArt.DrawMask(bitmap, cpuMask,
                settings.Parrot.CpuText == "top" ? 0 : size - textHeight, ParseColor(settings.Parrot.CpuTextColor, Color.White));
        }
        parrotSlot.Replace(bitmap);
        SetTooltip(parrot, $"CPU {overlay ?? MonitorMath.Round(cpu)}%", showsDigits: overlay.HasValue);
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
}
