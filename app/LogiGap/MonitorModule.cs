using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LogiGap.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LogiGap;

internal sealed class MonitorModule : IFeatureModule
{
    private readonly NotifyIcon mainIcon;
    private readonly NotifyIcon parrot;
    private readonly NotifyIcon cpuIcon, memoryIcon;
    private readonly IconSlot mainSlot, parrotSlot, cpuSlot, memorySlot;
    private readonly System.Windows.Forms.Timer sampleTimer = new();
    private readonly System.Windows.Forms.Timer frameTimer = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly Bitmap[] frames;
    private readonly bool coloredFrames;        // partyparrot.gif のコマ（もともと虹色）。塗り替えない
    private readonly ILog log;
    private readonly AcpiTemperature acpi = new();
    private long acpiReadAt = -1000;
    private bool acpiFailureLogged;
    private Bitmap[]? scaledFrames;              // アイコンの大きさに縮めたコマ（大きさが変わるまで使い回す）
    private (int Size, int Height, bool Stretch, int Widen) scaledKey;
    private MonitorSettings settings = new();
    private string statusText = "LogiGap: 待機中";
    private CpuTimes? previousCpu;
    private double cpu;
    private CpuTemperature? temperature;
    private uint? memoryLoad;
    private double usedGb, totalGb;
    private int frame;
    private long themeReadAt = -5000;
    private Color foreground = Color.White;
    private bool disposed;
    private bool settingsPending;
    private bool TempEnabled => settings.Enabled && settings.TempMemory.Enabled;
    private bool ParrotEnabled => settings.Enabled && settings.Parrot.Enabled;
    private bool CpuEnabled => settings.Enabled && settings.Cpu.Enabled;
    private bool MemoryEnabled => TempEnabled && settings.TempMemory.Split;

    private readonly TrayOrder order;

    public MonitorModule(AppSettings settings, ILog log, NotifyIcon mainIcon, ContextMenuStrip menu, TrayOrder order)
    {
        this.mainIcon = mainIcon; this.log = log; this.order = order;
        parrot = new() { ContextMenuStrip = menu };
        TrayIds.Check(mainIcon, TrayIds.Main, log);
        TrayIds.Check(parrot, TrayIds.Parrot, log);
        for (int i = 0; i < TrayIds.UnusedCount; i++)
        {
            using var unused = new NotifyIcon();
            TrayIds.Check(unused, TrayIds.UnusedFirst + i, log);
        }
        cpuIcon = new() { ContextMenuStrip = menu };
        TrayIds.Check(cpuIcon, TrayIds.Cpu, log);
        memoryIcon = new() { ContextMenuStrip = menu };
        TrayIds.Check(memoryIcon, TrayIds.Memory, log);
        order.Register(parrot, 0); order.Register(cpuIcon, 1);
        // 要確認: Program.cs は変更対象外なので、本体の rank はここで 2 として再登録する（現状は記録用）。
        order.Register(mainIcon, 2); order.Register(memoryIcon, 3);
        mainSlot = new(mainIcon); parrotSlot = new(parrot);
        cpuSlot = new(cpuIcon); memorySlot = new(memoryIcon);
        (frames, coloredFrames) = LoadFrames(log);
        sampleTimer.Tick += (_, _) => Sample();
        frameTimer.Tick += (_, _) => { frame = (frame + 1) % frames.Length; DrawParrot(); };
        SettingsChanged(settings);
    }

    public IEnumerable<ToolStripItem> CreateMenuItems() => [];

    public void SetStatus(string text)
    {
        if (disposed) return;
        statusText = text;
        UpdateTooltip();
        if (TrayInteraction.Busy) sampleTimer.Start(); // 全モニター無効時でも、ドラッグ終了後に状態の文を反映する。
    }

    public void SettingsChanged(AppSettings value)
    {
        if (disposed) return;
        sampleTimer.Stop(); frameTimer.Stop();
        settings = value.Monitor;
        acpiReadAt = elapsed.ElapsedMilliseconds - 1000;   // 読み方が変わってもすぐ読む
        sampleTimer.Interval = settings.SampleIntervalMs;
        settingsPending = true;
        sampleTimer.Start();
        Sample();
    }

    private void Sample()
    {
        if (settingsPending)
        {
            // 要確認: 並べ替え中の設定変更は保留し、終了後の周期で非表示化・ハンドル解放まで行う。
            if (TrayInteraction.Busy) return;
            settingsPending = false;
            if (!TempEnabled) mainSlot.Reset(SystemIcons.Application);
            if (!ParrotEnabled) { order.Want(parrot, false); parrotSlot.Reset(null); }
            if (!CpuEnabled) { order.Want(cpuIcon, false); cpuSlot.Reset(null); }
            if (!MemoryEnabled) { order.Want(memoryIcon, false); memorySlot.Reset(null); }
            if (ParrotEnabled) frameTimer.Start();
        }
        if (!TempEnabled && !ParrotEnabled && !CpuEnabled)
        {
            previousCpu = null;
            if (!TrayInteraction.Busy) { UpdateTooltip(); sampleTimer.Stop(); }
            return;
        }
        if (Native.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            var current = new CpuTimes(idle, kernel, user);
            // 要確認: 最初の CPU サンプルは比較対象がないため 0% とし、次の周期から差分を表示する。
            cpu = previousCpu is { } previous ? MonitorMath.CpuUsage(previous, current, cpu) : 0;
            previousCpu = current;
        }
        else previousCpu = null;
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        if (Native.GlobalMemoryStatusEx(ref memory))
        {
            memoryLoad = Math.Min(memory.MemoryLoad, 100);
            totalGb = memory.TotalPhys / 1073741824.0;
            usedGb = (memory.TotalPhys - Math.Min(memory.AvailPhys, memory.TotalPhys)) / 1073741824.0;
        }
        // 要確認: メモリ API が失敗した周期は、古い値や 0% を出さず -- / 取得不可と表示する。
        else memoryLoad = null;
        if (!TempEnabled) temperature = null;
        else if (settings.TempMemory.Source == "coretemp") temperature = ReadTemperature();
        // ACPI の温度は 1℃ 刻みでゆっくり変わるので、1 秒に 1 回だけ読む（CPU・メモリは sampleIntervalMs ごと）。
        else if (elapsed.ElapsedMilliseconds - acpiReadAt >= 1000)
        {
            acpiReadAt = elapsed.ElapsedMilliseconds;
            temperature = acpi.Read();
            if (temperature is null && !acpiFailureLogged) { acpiFailureLogged = true; log.Write("CPU 温度: ACPI の温度を読めません（Thermal Zone Information）"); }
        }
        ReadTheme();
        if (TrayInteraction.Busy) return;
        UpdateTooltip(); // 初めて表示する前に、新しいアイコンの説明文も設定する。
        if (TempEnabled) DrawTempMemory();
        if (CpuEnabled)
        {
            DrawBig(cpuSlot, MonitorMath.Round(cpu), ParseColor(settings.Cpu.Color, Color.FromArgb(100, 210, 255)));
            order.Want(cpuIcon, true);
        }
        if (MemoryEnabled)
        {
            DrawBig(memorySlot, memoryLoad is { } load ? (int)load : null, ParseColor(settings.TempMemory.MemoryColor, Color.White));
            order.Want(memoryIcon, true);
        }
        if (ParrotEnabled)
        {
            frameTimer.Interval = MonitorMath.FrameInterval(cpu);
            DrawParrot();
            order.Want(parrot, true);
        }
    }

    private static CpuTemperature? ReadTemperature()
    {
        // 要確認: 終了した Core Temp のマッピングを保持し続けないよう、各サンプルで開き直し、その場で閉じる。
        using var local = Native.OpenFileMapping(4, false, "CoreTempMappingObjectEx");
        if (!local.IsInvalid) return ReadMapping(local);
        using var global = Native.OpenFileMapping(4, false, @"Global\CoreTempMappingObjectEx");
        return global.IsInvalid ? null : ReadMapping(global);
    }

    private static CpuTemperature? ReadMapping(SafeFileHandle mapping)
    {
        // 必要な先頭部分だけをマップし、電力などの拡張フィールドは読まない。
        nint view = Native.MapViewOfFile(mapping, 4, 0, 0, 2688);
        if (view == 0) return null;
        try
        {
            byte[] bytes = new byte[2688];
            Marshal.Copy(view, bytes, 0, bytes.Length);
            return MonitorMath.ReadCoreTemp(bytes);
        }
        finally { Native.UnmapViewOfFile(view); }
    }

    private void ReadTheme()
    {
        if (elapsed.ElapsedMilliseconds - themeReadAt < 5000) return;
        themeReadAt = elapsed.ElapsedMilliseconds;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            foreground = key?.GetValue("SystemUsesLightTheme") is int value && value == 1 ? Color.Black : Color.White;
        }
        // 要確認: テーマを読めないときは、最後に取得できた色（初期値は白）を維持する。
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    private static int IconSize()
    {
        nint taskbar = Native.FindWindow("Shell_TrayWnd", null);
        uint dpi = taskbar == 0 ? 0 : Native.GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = Native.GetDpiForSystem();
        int size = Native.GetSystemMetricsForDpi(49, dpi);
        return size > 0 ? size : 16;
    }

    private static Bitmap Canvas(int size) => new(size, size, PixelFormat.Format32bppArgb);
    private static Color ParseColor(string text, Color fallback) =>
        MonitorColor.TryParse(text, out var c) ? Color.FromArgb(c.R, c.G, c.B) : fallback;

    private void DrawTempMemory()
    {
        if (TrayInteraction.Busy) return;   // 通知領域で並べ替え中は描き替えない
        if (settings.TempMemory.Split)
        {
            DrawBig(mainSlot, temperature is { } reading ? MonitorMath.Round(reading.Average) : null, ParseColor(settings.TempMemory.TempColor, Color.Red));
            return;
        }
        int size = IconSize(), wide = MonitorFont.TempMemoryHeight(size), height = DigitArt.Height(size, settings.DigitStyle, wide), row = (size - 1) / 2;
        using var bitmap = Canvas(size);
        string tempText = temperature is { } t ? DigitArt.Fit(MonitorMath.Round(t.Average), size, settings.DigitStyle, wide) : "--";
        DrawText(bitmap, tempText, height, (row - height) / 2, ParseColor(settings.TempMemory.TempColor, Color.Red));
        string memoryText = memoryLoad is { } load ? DigitArt.Fit((int)load, size, settings.DigitStyle, wide) : "--";
        DrawText(bitmap, memoryText, height, size - row + (row - height) / 2, ParseColor(settings.TempMemory.MemoryColor, Color.White));
        mainSlot.Replace(bitmap);
    }

    private void DrawBig(IconSlot slot, int? value, Color color)
    {
        if (TrayInteraction.Busy) return;
        int size = IconSize();
        var key = new BigDrawingKey(value, size, settings.DigitStyle, color.ToArgb());
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
        scaledKey = (size, imageHeight, stretch, widen);
        return scaledFrames = result;
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

    private void UpdateTooltip()
    {
        // ツールチップの文字を変えても通知領域が並びを作り直すので、並べ替え中は変えない。
        if (TrayInteraction.Busy) return;
        string memoryText = memoryLoad is { } load ? $"メモリ {load}%（{usedGb:F1}/{totalGb:F1} GB）" : "メモリ 取得不可";
        if (CpuEnabled) SetTooltip(cpuIcon, $"CPU {MonitorMath.Round(cpu)}%");
        if (MemoryEnabled) SetTooltip(memoryIcon, memoryText);
        // 11/20 ユーザーの指定：温度のアイコンの説明には温度だけを出す（LogiGap の状態はマウスの電池のアイコンへ移した。
        // BatteryModule.SetStatus）。2 段表示（split = false）のときだけメモリの行も付ける。
        // 温度を出さない設定では、本体はアプリの標準アイコンになるので、状態の文を出す。
        string text = statusText;
        if (TempEnabled)
        {
            bool coreTemp = settings.TempMemory.Source == "coretemp";
            text = temperature is { } t
                ? coreTemp ? $"CPU {MonitorMath.Round(t.Average)}℃（最高 {MonitorMath.Round(t.Maximum)}℃）" : $"CPU {MonitorMath.Round(t.Average)}℃（ACPI）"
                : coreTemp ? "CPU 温度なし（Core Temp の共有メモリが見つからない）" : "CPU 温度なし（ACPI の温度が読めない）";
            if (!settings.TempMemory.Split) text += "・" + memoryText;
        }
        SetTooltip(mainIcon, text);
    }

    private static void SetTooltip(NotifyIcon icon, string text)
    {
        if (TrayInteraction.Busy) return;
        string clipped = text.Length <= 127 ? text : text[..127];
        if (icon.Text != clipped) icon.Text = clipped;
    }

    private static (Bitmap[] Frames, bool Colored) LoadFrames(ILog log)
    {
        // 11/19 ユーザーが用意した本家のパーティーパロット（10 コマ、体の色が虹色に変わる）を優先して使う。
        string gif = Path.Combine(AppContext.BaseDirectory, "Assets", "parrot", "partyparrot.gif");
        if (File.Exists(gif))
        {
            try { return (LoadGif(gif), true); }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or OutOfMemoryException or UnauthorizedAccessException)
            { log.Write($"partyparrot.gif を読めないため、RunCat365 の絵を使います: {ex.Message}"); }
        }
        return (LoadPngFrames(log), false);
    }

    private static Bitmap[] LoadGif(string path)
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
                EditPixels(bitmap, pixels =>
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
            var fallback = Canvas(32);
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

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        sampleTimer.Stop(); frameTimer.Stop(); sampleTimer.Dispose(); frameTimer.Dispose();
        order.Want(parrot, false); order.Want(cpuIcon, false); order.Want(memoryIcon, false);
        mainSlot.Reset(SystemIcons.Application); parrotSlot.Reset(null); cpuSlot.Reset(null); memorySlot.Reset(null);
        parrot.ContextMenuStrip = null; cpuIcon.ContextMenuStrip = null; memoryIcon.ContextMenuStrip = null;
        parrot.Dispose(); cpuIcon.Dispose(); memoryIcon.Dispose();
        foreach (var bitmap in frames) bitmap.Dispose();
        if (scaledFrames != null) foreach (var bitmap in scaledFrames) bitmap.Dispose();
        acpi.Dispose();
        elapsed.Stop();
    }

    // NotifyIcon が参照を切り替えた後でのみ、所有している HICON を解放する。
    private readonly record struct BigDrawingKey(int? Value, int Size, string Style, int Color);

    private sealed class IconSlot(NotifyIcon target)
    {
        private nint handle;
        private Icon? wrapper;
        public BigDrawingKey? Key { get; set; }
        public bool[,]? BigMask { get; set; }
        public void Replace(Bitmap bitmap)
        {
            nint nextHandle = bitmap.GetHicon();
            Icon? next = null;
            try
            {
                next = Icon.FromHandle(nextHandle);
                target.Icon = next;
            }
            catch
            {
                next?.Dispose(); Native.DestroyIcon(nextHandle); throw;
            }
            Release(); handle = nextHandle; wrapper = next; Key = null; BigMask = null;
        }
        public void Reset(Icon? replacement) { target.Icon = replacement; Release(); Key = null; BigMask = null; }
        private void Release()
        {
            wrapper?.Dispose(); wrapper = null;
            if (handle != 0) { Native.DestroyIcon(handle); handle = 0; }
        }
    }

    // Windows のパフォーマンスカウンター（PDH）で ACPI の温度を読む。管理者権限もドライバーも要らない。
    private sealed class AcpiTemperature : IDisposable
    {
        private const uint FormatDouble = 0x00000200, FormatNoCap100 = 0x00008000, MoreData = 0x800007D2;
        private nint query, counter;
        public CpuTemperature? Read()
        {
            try
            {
                if (query == 0)
                {
                    if (PdhOpenQueryW(null, 0, out query) != 0) { query = 0; return null; }
                    if (PdhAddEnglishCounterW(query, @"\Thermal Zone Information(*)\Temperature", 0, out counter) != 0) { Close(); return null; }
                }
                if (PdhCollectQueryData(query) != 0) return null;
                uint size = 0;
                if (PdhGetFormattedCounterArrayW(counter, FormatDouble | FormatNoCap100, ref size, out _, 0) != MoreData || size == 0) return null;
                nint buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArrayW(counter, FormatDouble | FormatNoCap100, ref size, out uint count, buffer) != 0) return null;
                    // PDH_FMT_COUNTERVALUE_ITEM_W（x64）：名前のポインター 8、状態 4、詰め物 4、値 8 ＝ 24 バイト。
                    var kelvins = new List<double>();
                    for (int i = 0; i < count; i++)
                    {
                        nint item = buffer + i * 24;
                        uint status = (uint)Marshal.ReadInt32(item, 8);
                        if (status is 0 or 1) kelvins.Add(BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16)));
                    }
                    return MonitorMath.FromAcpi(kelvins);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException) { return null; }
        }
        private void Close() { if (query != 0) PdhCloseQuery(query); query = counter = 0; }
        public void Dispose() => Close();
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nint user, out nint query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nint user, out nint counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, nint buffer);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle OpenFileMapping(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint MapViewOfFile(SafeFileHandle mapping, uint access, uint offsetHigh, uint offsetLow, nuint bytes);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnmapViewOfFile(nint view);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint FindWindow(string className, string? title);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
        [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(nint icon);
    }
}
