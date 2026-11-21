// MX Master 4 の接続状態と肩代わり・引き継ぎを管理します。
// 有効条件や送信規則を変える場所です。純粋な状態機械としてテストします。
using System.Collections.Concurrent;

namespace PartsTray.Core;

public enum SessionState { Disconnected, Active, Retreated, Paused }

/// <summary>
/// Receive() commits state changes synchronously on the caller's receive thread.
/// Only Pump(), called by a separate worker, submits I/O and SendInput.
/// The gate linearizes a physical write submission against retreat/disconnect.
/// No I/O completion is awaited while holding the gate.
/// </summary>
public sealed partial class GapSession : INativeWheelBridge
{
    public static IReadOnlyList<ushort> ButtonOrder { get; } = Array.AsReadOnly<ushort>([0x52, 0xC4, 0x56, 0x53, 0xC3, 0x1A0]);
    private static readonly ushort[] FeatureOrder = [0x2201, 0x1B04, 0x2150, 0x1D4B];
    private readonly object gate = new();
    private readonly IReportTransport transport;
    private readonly IInputSink input;
    private readonly IClock clock;
    private readonly ILog log;
    private readonly Func<bool> optionsRunning;
    private readonly Func<DateTime?>? optionsStartedUtc;
    private readonly Func<DateTime> utcNow;
    private readonly DateTime createdUtc;
    private DateTime? lastOptionsConfigUtc;
    private GapSettings settings;
    private GapSettings sessionSettings;
    private WindowKeySettings windowKeys;   // ボタンの「本当の最大化」「切り替え」で送るキー。変更はすぐ反映する
    private SessionState state;
    private byte device;
    private int generation;
    private readonly Dictionary<byte, ushort> identities = [];
    private readonly Dictionary<ushort, byte> features = [];
    private readonly Dictionary<ushort, byte> optionsFlags = [];
    private readonly List<HidReport> earlyOptions = [];
    private readonly HashSet<ushort> owned = [];
    private HashSet<ushort> pressed = [];
    private HashSet<ushort> heldButtons = [];
    private readonly Queue<(int Generation, ushort Cid, ButtonSettings? Action, int Delta)> actions = [];
    private readonly Queue<Command> cleanup = [];
    private readonly Queue<byte> probes = [];
    private readonly HashSet<(string Receiver, byte Device)> previousActive = [];
    private string receiverIdentity = "";
    private Probe? probe;
    private readonly ThumbWheelConverter wheel = new();
    private volatile bool bridgeActive;
    private readonly ConcurrentQueue<int> nativeWheel = new();
    private readonly NativeWheelScaler nativeScaler = new();
    private IWheelSmoother smoother;
    private long bridgeReceived, bridgeReceivedTotal, bridgeSent, bridgeSentTotal;
    private readonly List<int> bridgeExamples = [];
    private bool bridgeFailureLogged;
    private Pending? pending;
    private int discoveryStep, applyStep, dpi;
    private bool blocked, thumbOwned, optionsThumb, cleanupDone, paused, startupCheck, inheritedActive;
    private bool ignoredThumbLogged;
    private TimeSpan linkedAt, retreatAt, activeAt;

    private sealed record Command(byte Device, byte Feature, byte Function, byte[] Parameters,
        string Name, bool Write, bool Cleanup, Func<bool> Allowed, Action<HidReport> Success,
        Action? Submitted = null);
    private sealed class Probe(byte device)
    {
        public byte Device { get; } = device;
        public bool PingConfirmed { get; set; }
        public byte NameFeature { get; set; }
        public byte? NameLength { get; set; }
        public List<byte> Name { get; } = [];
    }
    private sealed class Pending(Command command, int generation, TimeSpan deadline)
    {
        public Command Command { get; } = command;
        public int Generation { get; } = generation;
        public TimeSpan Deadline { get; set; } = deadline;
        public int Retries { get; set; }
        public bool Busy { get; set; }
    }

    public GapSession(IReportTransport transport, IInputSink input, IClock clock, ILog log, Func<bool> optionsRunning, AppSettings? config = null,
        Func<DateTime?>? optionsStartedUtc = null, Func<DateTime>? utcNow = null)
    {
        var c = config ?? new(); c.Validate();
        this.transport = transport; this.input = input; this.clock = clock; this.log = log;
        this.optionsRunning = optionsRunning;
        this.optionsStartedUtc = optionsStartedUtc;
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        createdUtc = this.utcNow();
        settings = sessionSettings = c.GapFiller;
        windowKeys = c.WindowKeys;
        smoother = CreateSmoother(settings.ThumbWheel);
        paused = !settings.Enabled;
        state = paused ? SessionState.Paused : SessionState.Disconnected;
    }
    private bool supportedMouseSeen;
    public bool SupportedMouseSeen { get { lock (gate) return supportedMouseSeen; } }
    public SessionState State { get { lock (gate) return state; } }
    public bool IsPaused { get { lock (gate) return paused; } }
    public int WorkerWaitMilliseconds
    {
        get
        {
            lock (gate)
            {
                TimeSpan? due = pending?.Deadline;
                if ((!paused || ListenWhilePaused) && pending == null && ((device == 0 && (probe != null || probes.Count > 0)) || cleanup.Count > 0))
                    return 1;
                if (!paused && state == SessionState.Retreated && !startupCheck && (!cleanupDone || bridgeActive))
                {
                    var deadline = retreatAt + TimeSpan.FromSeconds(sessionSettings.CleanupDelaySeconds);
                    if (!due.HasValue || deadline < due.Value) due = deadline;
                }
                if (!paused && smoother.NextDue is { } wheelDue && (!due.HasValue || wheelDue < due.Value))
                    due = wheelDue;
                return due.HasValue ? (int)Math.Clamp(Math.Ceiling((due.Value - clock.Now).TotalMilliseconds), 1, int.MaxValue) : Timeout.Infinite;
            }
        }
    }
    public event Action? Wake;
    public bool TryTakeHorizontal(int delta)
    {
        if (!bridgeActive || delta == 0) return false;
        try { nativeWheel.Enqueue(delta); }
        catch { return false; }
        try { Wake?.Invoke(); }
        catch { }
        return true;
    }
    public event Action<SessionState>? StateChanged;
    // ロック内から呼ぶため、購読側は待たずに UI スレッドへ投函するだけにする。
    public event Action<string>? SwitchNotice;
    // HID スレッド・gate 内。購読側は待たずに所有スレッドへ投函するだけ。
    public event Action<short>? ThumbWheelRotated;
    // 全 CID の読み取り専用スナップショット。肩代わりの所有ボタンには絞らない。
    public event Action<IReadOnlyList<ushort>>? PressedButtonsChanged;
    public IReadOnlyDictionary<ushort, byte> OptionsFlags { get { lock (gate) return new Dictionary<ushort, byte>(optionsFlags); } }

    public void UpdateSettings(AppSettings config)
    {
        config.Validate();
        lock (gate)
        {
            bool enabledChanged = settings.Enabled != config.GapFiller.Enabled;
            bool holdChanged = windowKeys.HoldButton != config.WindowKeys.HoldButton;
            settings = config.GapFiller;
            windowKeys = config.WindowKeys;
            // 要確認: 接続中の HID 設定変更は次の接続へ延期。速度・方向は即時反映する。
            wheel.Reset();
            nativeScaler.Reset();
            smoother = CreateSmoother(settings.ThumbWheel);
            if (enabledChanged) SetPaused(!settings.Enabled);
            if (paused && holdChanged)
            {
                Invalidate();
                if (ListenWhilePaused && device == 0) ProbeExisting();
            }
            log.Write("設定再読込: HID 設定は次の接続、ホイール速度・方向は即時反映");
        }
        Wake?.Invoke();
    }
    private static IWheelSmoother CreateSmoother(ThumbSettings thumb)
    {
        IWheelSmoother result = thumb.SmoothMode == "spring" ? new WheelSpringSmoother() : new WheelSmoother();
        result.Configure(thumb.SmoothMode == "spring" ? thumb.SpringMs : thumb.SmoothMs, thumb.SmoothIntervalMs);
        return result;
    }

    public void SetPaused(bool value)
    {
        lock (gate)
        {
            StopBridge(value ? "一時停止" : "再開");
            paused = value;
            if (value) smoother.Reset();
            if (device != 0) previousActive.Remove((receiverIdentity, device));
            probes.Clear(); probe = null;
            Invalidate(); cleanupDone = true; startupCheck = false;
            heldButtons.Clear();
            Change(value ? SessionState.Paused : SessionState.Retreated, value ? "ユーザー一時停止" : "再開: 次の接続を待つ");
        }
        Wake?.Invoke();
    }
    public void ReceiverLost()
    {
        lock (gate)
        {
            StopBridge("レシーバー切断");
            RememberSession();
            Invalidate(); device = 0; identities.Clear(); features.Clear(); probes.Clear(); probe = null; startupCheck = false;
            heldButtons.Clear();
            Change(paused ? SessionState.Paused : SessionState.Disconnected, "レシーバー切断/復帰時の再オープン");
        }
        Wake?.Invoke();
    }
    public void ReceiverOpened(string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(identity);
        lock (gate)
        {
            if (receiverIdentity == identity) return;
            ReceiverLost();
            receiverIdentity = identity;
        }
    }
    public void ProbeExisting()
    {
        lock (gate)
        {
            if ((paused && !ListenWhilePaused) || device != 0) return;
            Invalidate(); probe = null;
            probes.Clear();
            for (byte d = 1; d <= 6; d++) probes.Enqueue(d);
            log.Write("起動判定: slots 1〜6 ping、成功した番号だけ DEVICE_NAME で特定");
        }
        Wake?.Invoke();
    }

    public void Receive(ReadOnlySpan<byte> raw)
    {
        try
        {
            var report = HidReport.Parse(raw);
            if (report is null) return;
            lock (gate)
            {
                if (report.Kind == ReportKind.Connection && report.Device is >= 1 and <= 6)
                {
                    if (report.Wpid != 0xB042) previousActive.Remove((receiverIdentity, report.Device));
                    identities[report.Device] = report.Wpid;
                    if (report.Wpid == 0xB042 && report.LinkUp)
                    {
                        Begin(report.Device, false);
                    }
                    else if (report.Device == device)
                    {
                        StopBridge("リンク切断");
                        RememberSession();
                        if (report.Wpid != 0xB042) previousActive.Remove((receiverIdentity, report.Device));
                        Invalidate(); device = 0; startupCheck = false;
                        heldButtons.Clear();
                        Change(paused ? SessionState.Paused : SessionState.Disconnected, "マウスのリンク切断/機器変更");
                    }
                }
                if ((!paused || ListenWhilePaused) && report.Device == device)
                {
                    // Options+ の swid は起動ごとに変わる（0xA・0xB・0xC・0xE を確認）。0（通知）と 5（自分）以外はすべて他のソフトとして扱う。
                    if (!paused && report.Kind == ReportKind.Response && report.Swid != 0 && report.Swid != 5)
                    {
                        // Discovery may not yet know the relevant index. Recheck before any write.
                        if (discoveryStep < FeatureOrder.Length && earlyOptions.Count < 256) earlyOptions.Add(report);
                        ObserveOptions(report);
                    }
                    if (report.Kind == ReportKind.Notification)
                    {
                        if (device != 0) ObserveIgnoredInput(report);
                        if (state == SessionState.Active) ObserveInput(report);
                    }
                    if (blocked && report.Swid != 5 && report.Kind is ReportKind.Notification or ReportKind.Response)
                        blocked = false;
                }
                Match(report);
            }
        }
        catch (Exception ex) { log.Write($"受信処理エラー: {ex}"); }
        finally { Wake?.Invoke(); }
    }

    private void Begin(byte d, bool existing)
    {
        supportedMouseSeen = true; // 接続通知・従来の起動識別で確認済み。切断しても記憶する。
        StopBridge("新しい接続");
        RememberSession();
        inheritedActive = previousActive.Contains((receiverIdentity, d));
        Invalidate(); probes.Clear(); probe = null; device = d; features.Clear(); owned.Clear(); earlyOptions.Clear();
        heldButtons.Clear(); ignoredThumbLogged = false;
        optionsFlags.Clear(); optionsThumb = thumbOwned = cleanupDone = blocked = false;
        discoveryStep = applyStep = 0; linkedAt = clock.Now; sessionSettings = settings;
        dpi = sessionSettings.Dpi.Primary; startupCheck = !paused;
        Change(paused ? SessionState.Paused : SessionState.Retreated,
            existing ? "起動時の確認済みマウス: 横取り状態を確認" : $"リンク確立 dev={d} WPID=B042: 横取り状態を確認");
    }
    private void Invalidate()
    {
        int thumbCount = 0;
        foreach (var a in actions)
        {
            if (a.Cid != 0)
                log.Write($"未実行のボタン操作を破棄 CID={a.Cid:X4} 設定={a.Action!.Action} {a.Action.Keys}");
            else thumbCount++;
        }
        if (thumbCount > 0) log.Write($"未送信のサムホイール {thumbCount} 件を破棄");
        generation++; pending = null; actions.Clear(); cleanup.Clear(); pressed.Clear(); wheel.Reset();
    }
}
