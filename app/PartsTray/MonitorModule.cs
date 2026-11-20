// CPU・メモリ・温度の取得とタイマー・表示切替を管理します。
// 更新間隔や取得の流れを変えるときはここを編集します。
using System.Diagnostics;
using System.Runtime.InteropServices;
using PartsTray.Core;
using Microsoft.Win32.SafeHandles;

namespace PartsTray;

internal sealed partial class MonitorModule : IFeatureModule
{
    private readonly NotifyIcon mainIcon;
    private readonly NotifyIcon parrot;
    private readonly NotifyIcon cpuIcon, memoryIcon;
    private readonly TrayIconSlot<BigDrawingKey> mainSlot, parrotSlot, cpuSlot, memorySlot;
    private readonly System.Windows.Forms.Timer sampleTimer = new();
    private readonly System.Windows.Forms.Timer frameTimer = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private Bitmap[] frames;
    private bool coloredFrames;        // partyparrot.gif のコマ（もともと虹色）。塗り替えない
    private readonly ILog log;
    private readonly AcpiTemperature acpi = new();
    private long acpiReadAt = -1000;
    private bool acpiFailureLogged;
    private Bitmap[]? scaledFrames;              // アイコンの大きさに縮めたコマ（大きさが変わるまで使い回す）
    private (int Size, int Height, bool Stretch, int Widen) scaledKey;
    private MonitorSettings settings = new();
    private string statusText = "PartsTray";
    private CpuTimes? previousCpu;
    private double cpu;
    private CpuTemperature? temperature;
    private uint? memoryLoad;
    private double usedGb, totalGb;
    private int frame;
    private readonly TaskbarTheme theme;
    private Color foreground => Palette.ForTaskbar(Palette.White, theme.IsLight);   // 暗い：白、明るい：ほぼ黒（電池の絵と同じ色）
    private bool framesTheme;
    private bool disposed;
    private bool settingsPending;
    private bool TempEnabled => settings.Enabled && settings.TempMemory.Enabled;
    private bool ParrotEnabled => settings.Enabled && settings.Parrot.Enabled;
    private bool CpuEnabled => settings.Enabled && settings.Cpu.Enabled;
    private bool MemoryEnabled => TempEnabled && settings.TempMemory.Split;

    private readonly TrayOrder order;

    public MonitorModule(AppSettings settings, ILog log, NotifyIcon mainIcon, ContextMenuStrip menu, TrayOrder order, TaskbarTheme theme)
    {
        this.theme = theme;
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
        // 既存の rank を維持して本体を登録する（Windows の並びは強制変更しない）。
        order.Register(mainIcon, 2); order.Register(memoryIcon, 3);
        mainSlot = new(mainIcon); parrotSlot = new(parrot);
        cpuSlot = new(cpuIcon); memorySlot = new(memoryIcon);
        (frames, coloredFrames) = ParrotFrames.Load(log, theme.IsLight);
        framesTheme = theme.IsLight; theme.Changed += ThemeChanged;
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
            if (!TempEnabled) mainSlot.Reset(AppIcon.Small);
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
            DrawBig(cpuSlot, MonitorMath.Round(cpu), ParseColor(settings.Cpu.Color, Palette.Cyan));
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

    private void ThemeChanged()
    {
        if (disposed) return;
        // 並べ替え中はコマの更新も延期し、次の Sample / DrawParrot で追いつく。
        ReadTheme();
        Sample();
    }

    private void ReadTheme()
    {
        if (framesTheme == theme.IsLight || TrayInteraction.Busy) return;
        var loaded = ParrotFrames.Load(log, theme.IsLight);
        foreach (var old in frames) old.Dispose();
        if (scaledFrames != null) foreach (var old in scaledFrames) old.Dispose();
        scaledFrames = null; frames = loaded.Frames; coloredFrames = loaded.Colored;
        framesTheme = theme.IsLight;
    }

    private static int IconSize()
    {
        nint taskbar = Native.FindWindow("Shell_TrayWnd", null);
        uint dpi = taskbar == 0 ? 0 : Native.GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = Native.GetDpiForSystem();
        int size = Native.GetSystemMetricsForDpi(49, dpi);
        return size > 0 ? size : 16;
    }

    private void UpdateTooltip()
    {
        // ツールチップの文字を変えても通知領域が並びを作り直すので、並べ替え中は変えない。
        if (TrayInteraction.Busy) return;
        string memoryText = memoryLoad is { } load ? $"メモリ {load}%（{usedGb:F1}/{totalGb:F1} GB）" : "メモリ 取得不可";
        if (CpuEnabled) SetTooltip(cpuIcon, $"CPU {MonitorMath.Round(cpu)}%");
        if (MemoryEnabled) SetTooltip(memoryIcon, memoryText);
        // 11/20 ユーザーの指定：温度のアイコンの説明には温度だけを出す（PartsTray の状態はマウスの電池のアイコンへ移した。
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

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; theme.Changed -= ThemeChanged;
        sampleTimer.Stop(); frameTimer.Stop(); sampleTimer.Dispose(); frameTimer.Dispose();
        order.Want(parrot, false); order.Want(cpuIcon, false); order.Want(memoryIcon, false);
        mainSlot.Reset(AppIcon.Small); parrotSlot.Reset(null); cpuSlot.Reset(null); memorySlot.Reset(null);
        parrot.ContextMenuStrip = null; cpuIcon.ContextMenuStrip = null; memoryIcon.ContextMenuStrip = null;
        parrot.Dispose(); cpuIcon.Dispose(); memoryIcon.Dispose();
        foreach (var bitmap in frames) bitmap.Dispose();
        if (scaledFrames != null) foreach (var bitmap in scaledFrames) bitmap.Dispose();
        acpi.Dispose();
        elapsed.Stop();
    }

    // NotifyIcon が参照を切り替えた後でのみ、所有している HICON を解放する。
    private sealed record BigDrawingKey(int? Value, int Size, string Style, int Color);

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
    }
}
