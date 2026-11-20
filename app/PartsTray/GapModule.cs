// 受信機の接続監視と肩代わり・電池追跡のワーカーを管理します。
// 監視周期やモジュール間の接続を変える場所です。
using System.Diagnostics;
using System.Runtime.InteropServices;
using PartsTray.Core;
using Microsoft.Win32.SafeHandles;

namespace PartsTray;

internal interface IFeatureModule : IDisposable
{
    IEnumerable<ToolStripItem> CreateMenuItems();
    void SettingsChanged(AppSettings settings);
}

internal sealed class GapModule : IFeatureModule
{
    private sealed class Clock : IClock
    { private readonly Stopwatch watch = Stopwatch.StartNew(); public TimeSpan Now => watch.Elapsed; }
    private sealed class Transport : IReportTransport
    {
        private readonly object sendGate = new();
        private HidConnection? connection;
        public HidConnection? Connection
        {
            get { lock (sendGate) return connection; }
            set { lock (sendGate) connection = value; }
        }
        public void Send(byte[] report)
        {
            lock (sendGate) (connection ?? throw new IOException("レシーバーなし")).Send(report);
        }
    }
    private readonly Transport transport = new();
    private readonly ILog log;
    private readonly AutoResetEvent wake = new(false);
    private readonly HighResolutionTimer? timer;
    private bool timerFailureLogged;
    private bool optionsStartFailureLogged;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker, monitor;
    private readonly object connectionGate = new();
    private int reopen;
    private bool lost;
    private readonly LogitechBatteryTracker battery;
    private readonly System.Threading.Timer batteryTimer;
    public event Action<BatteryDevice>? BatteryChanged;
    public IReadOnlyList<BatteryDevice> Batteries => battery.Snapshot;
    public GapSession Session { get; }
    public bool SupportedMouseSeen => Session.SupportedMouseSeen || battery.SupportedMouseSeen;
    private bool enabled;
    public bool Enabled => enabled;   // 肩代わりが設定で有効か（UI スレッドで読む）
    private ToolStripMenuItem? pauseItem;
    public GapModule(AppSettings settings, ILog log)
    {
        this.log = log; enabled = settings.GapFiller.Enabled;
        timer = HighResolutionTimer.Create();
        if (timer == null) LogTimerFailure();
        Session = new(transport, new WindowsInput(), new Clock(), log, OptionsRunning, settings,
            optionsStartedUtc: OptionsStartedUtc);
        Session.Wake += Signal;
        battery = new(transport, new Clock(), settings.Battery);
        battery.ReceiverLost();
        battery.Changed += value => BatteryChanged?.Invoke(value);
        batteryTimer = new(_ =>
        {
            try { battery.Pump(); }
            catch (Exception ex) { log.Write($"電池の照会: {ex.Message}"); }
        }, null, 1000, 1000);
        worker = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    Session.Pump();
                    int ms = Session.WorkerWaitMilliseconds;
                    if (ms != Timeout.Infinite && ms <= 30 && timer != null)
                    {
                        if (timer.Arm(ms)) { WaitHandle.WaitAny([wake, timer]); continue; }
                        LogTimerFailure();
                    }
                    wake.WaitOne(ms);
                }
                catch (Exception ex) { log.Write($"ワーカー監督: {ex}"); wake.WaitOne(1000); }
            }
        });
        monitor = Task.Run(Monitor);
    }
    private void Signal() => wake.Set();
    private void LogTimerFailure()
    {
        if (timerFailureLogged) return;
        timerFailureLogged = true;
        log.Write("高精度タイマーを使えません: 横スクロールの分割送信は約 16ms 間隔になります");
    }

    private sealed class HighResolutionTimer : WaitHandle
    {
        private HighResolutionTimer(SafeWaitHandle handle) => SafeWaitHandle = handle;
        public static HighResolutionTimer? Create()
        {
            try
            {
                var handle = CreateWaitableTimerExW(0, null, 0x00000002, 0x001F0003);
                if (!handle.IsInvalid) return new(handle);
                handle.Dispose();
            }
            catch (EntryPointNotFoundException) { }
            return null;
        }
        public bool Arm(int ms)
        {
            long dueTime = -(ms * 10_000L);
            return SetWaitableTimerEx(SafeWaitHandle, in dueTime, 0, 0, 0, 0, 0);
        }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, in long dueTime, int period,
            nint completion, nint argument, nint wakeContext, uint tolerableDelay);
    }
    private static bool OptionsRunning()
    {
        var processes = Process.GetProcessesByName("logioptionsplus_agent");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private DateTime? OptionsStartedUtc()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("logioptionsplus_agent"); }
        catch (Exception ex) { LogOptionsStartFailure(ex); return null; }
        try
        {
            DateTime? latest = null;
            foreach (var process in processes)
            {
                try
                {
                    var started = process.StartTime.ToUniversalTime();
                    if (latest is null || started > latest.Value) latest = started;
                }
                catch (Exception ex) { LogOptionsStartFailure(ex); }
            }
            return latest;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private void LogOptionsStartFailure(Exception ex)
    {
        // 要確認: 取得失敗のログは列挙失敗も含め、このモジュールの生存中に 1 回だけとし、再接続ごとの重複を防ぐ。
        if (optionsStartFailureLogged) return;
        optionsStartFailureLogged = true;
        log.Write($"Options+ の起動時刻を読めません: {ex.Message}");
    }
    public void ResumeFromSleep() { Interlocked.Exchange(ref reopen, 1); }
    private async Task Monitor()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var endpoints = NativeHid.Find();
                lock (connectionGate)
                {
                    if (stop.IsCancellationRequested) break;
                    var existing = transport.Connection;
                    if (lost || Interlocked.Exchange(ref reopen, 0) != 0 || existing?.Endpoints != endpoints)
                    {
                        Session.ReceiverLost(); battery.ReceiverLost(); transport.Connection = null; existing?.Dispose(); lost = false;
                    }
                    if (endpoints != null && transport.Connection == null)
                    {
                        var connection = new HidConnection(endpoints, log);
                        connection.Received += report =>
                        {
                            lock (connectionGate)
                            {
                                if (transport.Connection != connection) return;
                                Session.Receive(report);
                                if (HidReport.Parse(report) is { } parsed) battery.Receive(parsed);
                            }
                        };
                        connection.Lost += () =>
                        {
                            lock (connectionGate)
                            {
                                if (transport.Connection != connection) return;
                                Session.ReceiverLost(); battery.ReceiverLost(); lost = true;
                            }
                        };
                        transport.Connection = connection;
                        Session.ReceiverOpened(endpoints.Short.Container.ToString("D"));
                        connection.Start();
                        Session.ProbeExisting();
                        battery.ReceiverOpened(endpoints.Short.Container.ToString("D"));
                        log.Write($"レシーバー open container={endpoints.Short.Container}");
                    }
                }
            }
            catch (Exception ex) { log.Write($"レシーバー監視: {ex}"); }
            try { await Task.Delay(1000, stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        var pause = pauseItem = new ToolStripMenuItem("つなぎ役を一時停止") { Visible = enabled };
        pause.Click += (_, _) => { if (!enabled) return; Session.SetPaused(!Session.IsPaused); pause.Text = Session.IsPaused ? "つなぎ役を再開" : "つなぎ役を一時停止"; };
        pause.OwnerChanged += (_, _) => { if (pause.Owner is ContextMenuStrip menu) menu.Opening += (_, _) => pause.Text = Session.IsPaused ? "つなぎ役を再開" : "つなぎ役を一時停止"; };
        yield return pause;
    }
    public void SettingsChanged(AppSettings settings)
    {
        enabled = settings.GapFiller.Enabled;
        if (pauseItem != null) pauseItem.Visible = enabled;
        Session.UpdateSettings(settings);
        battery.UpdateSettings(settings.Battery);
    }
    public void Dispose()
    {
        stop.Cancel(); Session.SetPaused(true); wake.Set();
        batteryTimer.Dispose(); battery.ReceiverLost();
        lock (connectionGate) { transport.Connection?.Dispose(); transport.Connection = null; }
        Task.WaitAll([worker, monitor], TimeSpan.FromSeconds(3));
        // 要確認: 終了待ちがタイムアウトした場合は、待機中のハンドルを閉じず worker 終了後に解放する。
        if (worker.IsCompleted) timer?.Dispose();
        else _ = worker.ContinueWith(_ => timer?.Dispose(), TaskScheduler.Default);
        Session.Wake -= Signal;
    }
}
