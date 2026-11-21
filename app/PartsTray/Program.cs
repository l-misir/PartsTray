// 起動・設定の再読込・トレイメニューと各モジュールをつなぎます。
// 起動時の構成や設定画面の開き方を変える場所です。
using System.Diagnostics;
using System.Security.Principal;
using PartsTray.Core;
using Microsoft.Win32;

namespace PartsTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var log = new FileLog(Path.Combine(AppContext.BaseDirectory, "logs"));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => log.Write($"UI エラー: {e.Exception}");
        TaskScheduler.UnobservedTaskException += (_, e) => { log.Write($"非同期エラー: {e.Exception}"); e.SetObserved(); };
        // 裏のスレッドの例外でアプリが落ちるときは、記録の書き出し（別のスレッド）が間に合わない。その場で別のファイルに書く。
        // 11/21：夜中に PartsTray が記録を残さずに消えていたため。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "logs", $"fatal_{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 落ちる原因の例外: {e.ExceptionObject}{Environment.NewLine}"); }
            catch (Exception) { }
        };
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            string suffix = identity.User?.Value ?? throw new InvalidOperationException("ユーザー SID がありません");
            AdministratorRestart.WaitForParent(args);
            using var instance = new SingleInstance(suffix);
            if (!instance.Acquire(AdministratorRestart.IsRestart(args) ? 5000 : 0)) { instance.Show.Set(); return; }
            string? startupNotice = null;
            try
            {
                string config = Path.Combine(AppContext.BaseDirectory, "config.json");
                bool elevate = File.Exists(config) && AppSettings.Load(config).RunAsAdministrator;
                if (elevate && !ProcessElevation.IsAdministrator && !AdministratorRestart.IsRestart(args) &&
                    AdministratorRestart.TryRestart(instance, log, text => startupNotice = text)) return;
            }
            catch (Exception ex) { log.Write($"管理者起動設定の読込失敗: {ex.Message}"); }
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; }
            catch (Exception ex) { log.Write($"優先度設定失敗: {ex.Message}"); }
            PowerThrottling.OptOut(log);
            using var context = new TrayContext(log, instance.Show, instance, startupNotice);
            Application.Run(context);
        }
        catch (Exception ex) { log.Write($"起動/終了エラー: {ex}"); }
    }
}

// Windows 11 の省電力の仕組み（電源を外しているときなどに、裏のアプリの処理やタイマーを後回しにする）から
// PartsTray だけを外す。アイコンの更新（0.2 秒ごと）と、肩代わり中のサムホイールの送り出し（8ms ごと）が遅れないようにする。
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

internal sealed partial class TrayContext : ApplicationContext
{
    private readonly ILog log;
    private readonly string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem status = new() { Enabled = false };
    private readonly Form dispatcher = new() { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.FixedToolWindow };
    private readonly GapModule gap;
    private readonly GestureModule gestures;
    private readonly MonitorModule monitor;
    private readonly TaskbarTheme theme;
    private readonly BatteryModule battery;
    private readonly WindowKeysModule windowKeys;   // 本当の最大化・最大化しているアプリの切り替え
    private readonly TrayOrder trayOrder;
    private readonly List<IFeatureModule> modules;
    private readonly System.Windows.Forms.Timer settingsTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer clickTimer = new() { Interval = SystemInformation.DoubleClickTime };
    private SettingsDestination menuDestination = new(), clickDestination = new();
    private readonly RegisteredWaitHandle showWait;
    private DateTime configStamp;
    private bool disposing;
    private SettingsWindow? settingsWindow;
    private bool openingSettings;

    public TrayContext(ILog log, EventWaitHandle show, SingleInstance instance, string? startupNotice)
    {
        this.log = log; this.instance = instance;
        autoStartManager = new(log);
        _ = dispatcher.Handle;
        AppSettings settings;
        bool firstRun = !File.Exists(configPath);   // 設定ファイルがまだない＝初めての起動（Load が既定値で作る）
        try { settings = AppSettings.Load(configPath); }
        catch (Exception ex) { log.Write($"設定エラー、安全のため一時停止: {ex}"); settings = new() { GapFiller = new() { Enabled = false }, Gesture = new() { Enabled = false }, TitleBar = new() { ShiftClick = "None", CtrlClick = "None", MiddleClick = "None", MinimizeButtonRightClick = "None" }, Wheel = new() { FastScrollOnScrollBar = false, FocusUnderCursor = false }, MoveDisabledWindows = false }; }
        configStamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : default;
        icon = new() { Icon = AppIcon.Small, Visible = true, ContextMenuStrip = menu };
        gap = new(settings, log);
        gestures = new(settings, log, Post, text => Post(() => icon.ShowBalloonTip(5000, "PartsTray", text, ToolTipIcon.Info)), wheelBridge: gap.Session);
        trayOrder = new(log);
        trayOrder.Register(icon, 1);   // 温度・メモリ（オウムの右）
        trayOrder.Want(icon, true);
        theme = new(settings.Monitor.TaskbarTheme);
        monitor = new(settings, log, icon, menu, trayOrder, theme, BindSettingsIcon);
        battery = new BatteryModule(settings, log, gap, menu, Post, trayOrder, theme, BindSettingsIcon);
        windowKeys = new(settings, log);
        gap.Session.ThumbWheelRotated += DeviceThumbWheel;
        gap.Session.PressedButtonsChanged += DevicePressedButtons;
        modules = [gap, gestures, monitor, battery, windowKeys];
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); OpenSettings(clickDestination); };
        BindSettingsIcon(icon, SettingsIconKind.Temperature);
        menu.Items.Add(status);
        menu.Items.Add("設定…", null, (_, _) => OpenSettings(menuDestination));
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
            try { autoStart.Checked = ReadAutoStart(); }
            catch (Exception ex) { log.Write($"自動起動の読取失敗: {ex.Message}"); }
        };
        autoStart.Click += (_, _) =>
        {
            try { WriteAutoStart(!ReadAutoStart()); autoStart.Checked = ReadAutoStart(); }
            catch (Exception ex) { MessageBox.Show("自動起動を変更できませんでした。\n" + ex.Message, "PartsTray", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        gap.Session.StateChanged += StateChanged;
        gap.Session.SwitchNotice += SwitchNotice;
        StateChanged(gap.Session.State);
        currentSettings = settings;
        autoStartManager.Reconcile(settings.RunAsAdministrator);
        if (startupNotice != null) Post(() => SwitchNotice(startupNotice));
        SystemEvents.PowerModeChanged += PowerChanged;
        showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => Post(OpenSettings), null, Timeout.Infinite, false);
        settingsTimer.Tick += (_, _) => Reload(); settingsTimer.Start();
        if (firstRun) ShowFirstRunGuide();
    }
    // 初めての起動の案内（11/20）。Windows は新しいアプリの通知アイコンを最初は隠すので、
    // 何も知らずに起動すると「何も起きない」ように見える。アイコンが並ぶのを少し待ってから通知を出す。
    private void ShowFirstRunGuide()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop(); timer.Dispose();
            if (disposing) return;
            icon.ShowBalloonTip(15000, "PartsTray を起動しました",
                "アイコンは、タスクバーの右にある「∧」（隠れているインジケーター）の中にあります。タスクバーへドラッグすると、いつも表示されます。温度のアイコンをクリックすると設定が開きます。",
                ToolTipIcon.Info);
            log.Write("初めての起動の案内を出しました");
        };
        timer.Start();
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
    private void DevicePressedButtons(IReadOnlyList<ushort> buttons) => Post(() =>
    {
        if (!disposing) windowKeys.ReceivePressedButtons(buttons);
    });
    private void DeviceThumbWheel(short rotation) => Post(() =>
    {
        if (!disposing && !windowKeys.ReceiveDeviceRotation(rotation)) gestures.ReceiveDeviceRotation(rotation);
    });

    private void SwitchNotice(string text) => Post(() => icon.ShowBalloonTip(3000, "PartsTray", text, ToolTipIcon.Info));
    private void StateChanged(SessionState value) => Post(() =>
    {
        string text = value switch
        {
            SessionState.Active => "肩代わり中", SessionState.Retreated => "Options+ に引き継ぎ済み",
            SessionState.Paused => "一時停止中", _ => "待機中"
        };
        // 状態の文は、温度を出さない設定のときの本体の説明と、マウスの電池のアイコンの説明に出す（11/20 ユーザーの指定）。
        // 肩代わりを切っているとき（配った人の既定）は、肩代わりの状態を出さない。
        bool on = gap.Enabled;
        status.Text = $"肩代わり: {text}"; status.Visible = on;
        monitor.SetStatus(on ? $"PartsTray（肩代わり: {text}）" : "PartsTray");
        battery.SetStatus(on ? $"肩代わり: {text}" : "");
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Resume) gap.ResumeFromSleep(); }
    private void Reload()
    {
        settingsWindow?.RefreshSupportedMouse(gap.SupportedMouseSeen);
        try
        {
            var stamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : default;
            if (stamp == configStamp) return;
            var settings = AppSettings.Load(configPath);
            if (settings.RunAsAdministrator != currentSettings.RunAsAdministrator)
                autoStartManager.Reconcile(settings.RunAsAdministrator);
            currentSettings = settings;
            theme.SettingsChanged(settings.Monitor.TaskbarTheme);
            foreach (var module in modules) module.SettingsChanged(settings);
            StateChanged(gap.Session.State);   // 肩代わりの有効・無効が変わったとき、状態の文の出し分けを直す
            configStamp = File.GetLastWriteTimeUtc(configPath);
        }
        catch (Exception ex) { log.Write($"設定再読込失敗（前の設定を維持）: {ex.Message}"); }
    }
    protected override void Dispose(bool disposingManaged)
    {
        if (disposingManaged && !disposing)
        {
            disposing = true; settingsTimer.Stop(); settingsTimer.Dispose(); clickTimer.Dispose(); showWait.Unregister(null);
            settingsWindow?.Dispose(); settingsWindow = null;
            SystemEvents.PowerModeChanged -= PowerChanged;
            gap.Session.StateChanged -= StateChanged;
            gap.Session.SwitchNotice -= SwitchNotice;
            gap.Session.ThumbWheelRotated -= DeviceThumbWheel;
            gap.Session.PressedButtonsChanged -= DevicePressedButtons;
            foreach (var module in modules) module.Dispose();
            theme.Dispose(); trayOrder.Dispose();
            icon.Visible = false; icon.Dispose(); menu.Dispose(); dispatcher.Dispose();
        }
        base.Dispose(disposingManaged);
    }
}
