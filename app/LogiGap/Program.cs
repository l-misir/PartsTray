using System.Diagnostics;
using System.Security.Principal;
using LogiGap.Core;
using Microsoft.Win32;

namespace LogiGap;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var log = new FileLog(Path.Combine(AppContext.BaseDirectory, "logs"));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Write($"UI エラー: {e.Exception}");
        TaskScheduler.UnobservedTaskException += (_, e) => { log.Write($"非同期エラー: {e.Exception}"); e.SetObserved(); };
        try
        {
            string suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\LogiGap.Show.{suffix}");
            using var mutex = new Mutex(true, $"Local\\LogiGap.Instance.{suffix}", out bool first);
            if (!first) { show.Set(); return; }
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; }
            catch (Exception ex) { log.Write($"優先度設定失敗: {ex.Message}"); }
            PowerThrottling.OptOut(log);
            try { using var context = new TrayContext(log, show); Application.Run(context); }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex) { log.Write($"起動/終了エラー: {ex}"); }
    }
}

// Windows 11 の省電力の仕組み（電源を外しているときなどに、裏のアプリの処理やタイマーを後回しにする）から
// LogiGap だけを外す。アイコンの更新（0.2 秒ごと）と、肩代わり中のサムホイールの送り出し（8ms ごと）が遅れないようにする。
// 11/19 ユーザー：「Core Temp は電源非接続時や高負荷時に表示が更新されないことがある」。
internal static class PowerThrottling
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct State { public uint Version, ControlMask, StateMask; }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(nint process, int infoClass, ref State info, uint size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    public static void OptOut(ILog log)
    {
        // ProcessPowerThrottling = 4。ControlMask に入れて StateMask を 0 にすると、その項目の省電力を受けない。
        // 0x1 = 処理速度（EcoQoS）、0x4 = タイマーの精度を落とすこと。
        var state = new State { Version = 1, ControlMask = 0x1 | 0x4, StateMask = 0 };
        if (!SetProcessInformation(GetCurrentProcess(), 4, ref state, (uint)System.Runtime.InteropServices.Marshal.SizeOf<State>()))
            log.Write($"省電力の対象から外す設定に失敗: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        else log.Write("省電力の対象から外しました（処理速度・タイマー）");
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly ILog log;
    private readonly string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem status = new() { Enabled = false };
    private readonly Form dispatcher = new() { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.FixedToolWindow };
    private readonly GapModule gap;
    private readonly GestureModule gestures;
    private readonly MonitorModule monitor;
    private readonly BatteryModule battery;
    private readonly TrayOrder trayOrder;
    private readonly List<IFeatureModule> modules;
    private readonly System.Windows.Forms.Timer settingsTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer clickTimer = new() { Interval = SystemInformation.DoubleClickTime };
    private long doubleClickAt = long.MinValue / 2;
    private readonly RegisteredWaitHandle showWait;
    private DateTime configStamp;
    private bool disposing;

    public TrayContext(ILog log, EventWaitHandle show)
    {
        this.log = log;
        _ = dispatcher.Handle;
        AppSettings settings;
        try { settings = AppSettings.Load(configPath); }
        catch (Exception ex) { log.Write($"設定エラー、安全のため一時停止: {ex}"); settings = new() { GapFiller = new() { Enabled = false }, Gesture = new() { Enabled = false }, TitleBar = new() { ShiftClick = "None", CtrlClick = "None", MiddleClick = "None", MinimizeButtonRightClick = "None" }, Wheel = new() { FastScrollOnScrollBar = false, FocusUnderCursor = false }, MoveDisabledWindows = false }; }
        configStamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : default;
        icon = new() { Icon = SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        gap = new(settings, log);
        gestures = new(settings, log, Post, text => Post(() => icon.ShowBalloonTip(5000, "LogiGap", text, ToolTipIcon.Info)), wheelBridge: gap.Session);
        trayOrder = new(log);
        trayOrder.Register(icon, 1);   // 温度・メモリ（オウムの右）
        trayOrder.Want(icon, true);
        monitor = new(settings, log, icon, menu, trayOrder);
        battery = new BatteryModule(settings, log, gap, menu, Post, trayOrder);
        modules = [gap, gestures, monitor, battery];
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); Open(configPath); };
        icon.MouseDoubleClick += (_, _) => { doubleClickAt = Environment.TickCount64; clickTimer.Stop(); };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && Environment.TickCount64 - doubleClickAt > SystemInformation.DoubleClickTime) clickTimer.Start();
            else if (e.Button == MouseButtons.Middle) gestures.TogglePause();
        };
        menu.Items.Add(status);
        foreach (var module in modules) menu.Items.AddRange(module.CreateMenuItems().ToArray());
        menu.Items.Add("設定ファイルを開く", null, (_, _) => Open(configPath));
        menu.Items.Add("ログフォルダを開く", null, (_, _) =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "logs"); Directory.CreateDirectory(path); Open(path);
        });
        var autoStart = new ToolStripMenuItem("Windows 起動時に自動起動");
        menu.Items.Add(autoStart);
        menu.Opening += (_, _) =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            autoStart.Checked = key?.GetValue("LogiGap") is string;
        };
        autoStart.Click += (_, _) =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (autoStart.Checked) key.DeleteValue("LogiGap", false);
            else key.SetValue("LogiGap", $"\"{Environment.ProcessPath}\"");
        };
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        gap.Session.StateChanged += StateChanged;
        gap.Session.SwitchNotice += SwitchNotice;
        StateChanged(gap.Session.State);
        SystemEvents.PowerModeChanged += PowerChanged;
        showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => Post(() => Open(configPath)), null, Timeout.Infinite, false);
        settingsTimer.Tick += (_, _) => Reload(); settingsTimer.Start();
    }
    private void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { log.Write($"開く操作: {ex}"); }
    }
    private void Post(Action action)
    {
        if (disposing || dispatcher.IsDisposed) return;
        try { dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { }
    }
    private void SwitchNotice(string text) => Post(() => icon.ShowBalloonTip(3000, "LogiGap", text, ToolTipIcon.Info));
    private void StateChanged(SessionState value) => Post(() =>
    {
        string text = value switch
        {
            SessionState.Active => "肩代わり中", SessionState.Retreated => "Options+ に引き継ぎ済み",
            SessionState.Paused => "一時停止中", _ => "待機中"
        };
        // 状態の文は、温度を出さない設定のときの本体の説明と、マウスの電池のアイコンの説明に出す（11/20 ユーザーの指定）。
        status.Text = text; monitor.SetStatus($"LogiGap: {text}"); battery.SetStatus($"LogiGap: {text}");
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Resume) gap.ResumeFromSleep(); }
    private void Reload()
    {
        try
        {
            var stamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : default;
            if (stamp == configStamp) return;
            var settings = AppSettings.Load(configPath);
            foreach (var module in modules) module.SettingsChanged(settings);
            configStamp = File.GetLastWriteTimeUtc(configPath);
        }
        catch (Exception ex) { log.Write($"設定再読込失敗（前の設定を維持）: {ex.Message}"); }
    }
    protected override void Dispose(bool disposingManaged)
    {
        if (disposingManaged && !disposing)
        {
            disposing = true; settingsTimer.Stop(); settingsTimer.Dispose(); clickTimer.Dispose(); showWait.Unregister(null);
            SystemEvents.PowerModeChanged -= PowerChanged;
            gap.Session.StateChanged -= StateChanged;
            gap.Session.SwitchNotice -= SwitchNotice;
            foreach (var module in modules) module.Dispose();
            trayOrder.Dispose();
            icon.Visible = false; icon.Dispose(); menu.Dispose(); dispatcher.Dispose();
        }
        base.Dispose(disposingManaged);
    }
}
