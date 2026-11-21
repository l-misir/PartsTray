// 電池の一覧を保持し、枠への割り当てと説明文を管理します。
// 表示対象・並び・更新周期を変えるときはここを編集します。
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal sealed class BatteryModule : IFeatureModule
{
    private readonly ILog log;
    private readonly TaskbarTheme theme;
    private readonly GapModule gap;
    private readonly ContextMenuStrip menu;
    private readonly Action<Action> post;
    private readonly Dictionary<string, BatteryDevice> devices = [];
    private readonly TrayIconSlot<DrawingKey>[] pool;   // 電池のアイコンの枠（最大 12 台）。左から 2 番目以降（オウム 0、温度・メモリ 1 の右）
    private readonly System.Threading.Timer bluetoothTimer;
    private readonly System.Windows.Forms.Timer themeTimer = new() { Interval = 5000 };
    // 残量 100% の虹の輪を回すタイマーと、回すコマのアイコンの置き場（同じコマを毎回作り直さないため）。
    // 11/20 実機：24 コマで毎回絵からアイコンを作ると、2 台回すだけで CPU がコア 1 つの 5% 増えたので、コマを減らして使い回す。
    private const int RainbowSteps = 16;   // 1 周のコマ数
    private readonly System.Windows.Forms.Timer rainbowTimer = new();
    private readonly System.Diagnostics.Stopwatch rainbowClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<DrawingKey, (Icon Icon, nint Handle)> rainbowFrames = [];
    // AirPods の電池（Windows が持たないので、AirPods が飛ばす電波から読む。AirPodsWatcher.cs）。
    private readonly AirPodsWatcher airPods;
    private readonly BluetoothConnectionWatcher connections;   // つながった・切れたの知らせ（すぐ読み直すため）
    private long burstUntil;   // 知らせを受けてから、この時刻（Environment.TickCount64）までは 3 秒ごとに読み直す
    private readonly SonyBatteryPoller sony;
    private readonly System.Windows.Forms.Timer sonyTimer = new();   // Sony のヘッドホンに聞きに行く間隔（Bluetooth の読み直しとは別）   // Sony のヘッドホンに直接聞く（充電の状態も分かる）
    private readonly AppleDeviceBattery apple;   // USB でつないだ iPhone・iPad（Apple のサービス経由）
    private readonly PortableDeviceBattery portable;   // USB でつないだ Android など（Windows のファイルのやり取りの仕組み＝WPD）
    private readonly BatteryTrend trend = new();   // 残量の増え方から充電中を推測する（Windows から読む Bluetooth 機器）
    private BatterySettings settings = new();
    private string? statusText;
    private long generation;
    private int polling;
    private volatile bool disposed;

    public BatteryModule(AppSettings settings, ILog log, GapModule gap, ContextMenuStrip menu, Action<Action> post, TrayOrder order, TaskbarTheme theme, Action<NotifyIcon, SettingsIconKind> bindSettings)
    {
        this.theme = theme; theme.Changed += Render;
        this.log = log; this.gap = gap; this.menu = menu; this.post = post;
        // 番号の表は TrayIds。MonitorModule が 10 まで確保し、電池は 11〜22（従来の 11〜13 を維持）。
        pool = Enumerable.Range(0, TrayIds.BatteryCount).Select(i => new TrayIconSlot<DrawingKey>(menu, order, 2 + i)).ToArray();
        for (int i = 0; i < pool.Length; i++)
        { TrayIds.Check(pool[i].Target, TrayIds.BatteryFirst + i, log); bindSettings(pool[i].Target, SettingsIconKind.Battery); }
        bluetoothTimer = new(_ => PollBluetooth(), null, Timeout.Infinite, Timeout.Infinite);
        gap.BatteryChanged += LogitechChanged;
        airPods = new(log);
        airPods.Changed += reading => post(() => AirPodsChanged(reading));
        connections = new(log);
        apple = new(log);
        apple.Read += list => post(() => UsbRead("apple:", list));
        portable = new(log);
        portable.Read += list => post(() => UsbRead("usb:", list));
        sony = new(log, post);
        sony.Updated += SonyUpdated;
        sonyTimer.Tick += (_, _) => { if (!disposed && this.settings.Enabled && this.settings.Bluetooth && this.settings.SonyDirect) sony.Consider(devices.Values.Where(IsSonyCandidate)); };
        connections.Changed += Burst;
        foreach (var value in gap.Batteries) devices[value.Id] = value;
        // 5 秒ごとに描き直す（通知領域で並べ替え中に描き替えを見送った分を、離した後に反映するため）。
        // 明暗の変更は共通の TaskbarTheme が通知する。ここでは並べ替え後の再描画を補う。
        themeTimer.Tick += (_, _) => Render();
        rainbowTimer.Tick += (_, _) => Render();   // 虹の輪がある間だけ動く（Render の最後で入り切り）
        SettingsChanged(settings); themeTimer.Start();
    }
    public IEnumerable<ToolStripItem> CreateMenuItems() => [];
    // UI スレッドから呼ぶ。内部の辞書を公開せず、名前だけの読み取り専用スナップショットを返す。
    // 電池の値が一度も取れていない機器（受信機の空いた番号に付く「Logitech 4」などの仮の名前）は出さない（11/20 実機で混じっていた）。
    public IReadOnlyList<string> GetDeviceNames() => Array.AsReadOnly(devices.Values.Where(d => d.Reading != null).Select(d => d.Name)
        .Distinct(StringComparer.Ordinal).ToArray());
    public IReadOnlyList<BatteryDevice> GetDevices() => Array.AsReadOnly(devices.Values.Where(d => d.Reading != null).ToArray());
    // 肩代わりの状態（「肩代わり: 肩代わり中」など。肩代わりを切っているときは空）。TrayContext が UI スレッドで渡す。マウスのアイコンの説明に出す。
    public void SetStatus(string text)
    {
        if (disposed || statusText == text) return;
        statusText = text; Render();
    }
    private void LogitechChanged(BatteryDevice value) => post(() =>
    {
        if (disposed) return;
        devices[value.Id] = value; Render();
    });
    public void SettingsChanged(AppSettings value)
    {
        if (disposed) return;
        settings = value.Battery;
        rainbowTimer.Interval = Math.Max(15, settings.RainbowLapMs / RainbowSteps);
        sony.Every = TimeSpan.FromSeconds(settings.SonyPollSeconds);
        sonyTimer.Interval = settings.SonyPollSeconds * 1000; sonyTimer.Enabled = settings.Enabled && settings.Bluetooth && settings.SonyDirect;
        Interlocked.Increment(ref generation);
        bluetoothTimer.Change(settings.Enabled && settings.Bluetooth ? 0 : Timeout.Infinite, BluetoothPeriod());
        UpdateAirPodsWatcher();
        apple.SetEnabled(settings.Enabled && settings.AppleDevices);
        portable.SetEnabled(settings.Enabled && settings.AndroidDevices);
        Render();
    }
    // AirPods の電波を受け取っている間は、つながっているかを 5 秒ごとに確かめる（ふだんは BluetoothPollSeconds ごと）。
    // 切断に早く気づいて受け取りを止め、アイコンを隠すため（11/20 ユーザー：「つながってないのに他の人のやつが表示されたりしないですよね？」）。
    private const int AirPodsPollMs = 5000;
    private bool airPodsActive;
    private int BluetoothPeriod() => airPodsActive ? AirPodsPollMs : settings.BluetoothPollSeconds * 1000;
    // AirPods がこの PC につながっている間だけ、電波を受け取る（周りの人の AirPods の電波をむやみに拾わないため）。
    // つながっていないあいだは受け取らないので、他の人の AirPods がアイコンに出ることはない（切断中のイヤホンは BatteryLayout が隠す）。
    private void UpdateAirPodsWatcher()
    {
        bool run = !disposed && settings.Enabled && settings.Bluetooth && settings.AirPods &&
            devices.Values.Any(d => d.Connected && d.Id.StartsWith("bluetooth:", StringComparison.Ordinal) && BluetoothBatteryReader.IsAirPods(d.Name));
        airPods.SetRunning(run);
        if (run == airPodsActive || disposed) return;
        airPodsActive = run;
        // つないだ直後の 3 秒ごとの読み直し（Burst）の途中なら、間隔はそのまま。終わるときに PollBluetooth がふだんの間隔に戻す。
        if (settings.Enabled && settings.Bluetooth && Volatile.Read(ref burstUntil) == 0) bluetoothTimer.Change(BluetoothPeriod(), BluetoothPeriod());
    }
    // Bluetooth の機器がつながった・切れたら、1.5 秒後から 20 秒間、3 秒ごとに読み直す。
    // つないだ直後は、Windows が電池の値や音の出入り口を用意するまで数秒かかるため、1 回では足りない。
    private void Burst()
    {
        if (disposed || !settings.Enabled || !settings.Bluetooth) return;
        Volatile.Write(ref burstUntil, Environment.TickCount64 + 20000);
        bluetoothTimer.Change(1500, 3000);
    }
    // Sony の可能性がある機器：Windows から読む Bluetooth のヘッドホン・イヤホン（AirPods は除く）。通り道がなければ一度で外れる。
    private static bool IsSonyCandidate(BatteryDevice d) => d.Id.StartsWith("bluetooth:", StringComparison.Ordinal) &&
        !BluetoothBatteryReader.IsAirPods(d.Name) && d.Kind is BatteryDeviceKind.Headphones or BatteryDeviceKind.Earbuds;
    private void SonyUpdated()
    {
        if (disposed || !settings.SonyDirect) return;
        foreach (var (id, device) in devices.ToArray())
            if (device.Connected && sony.Latest(id) is { } direct) devices[id] = device with { Reading = direct, UpdatedAt = DateTime.UtcNow };
        Render();
    }
    // USB でつながっている iPhone・iPad（apple:）、Android など（usb:）の一覧。一覧にない機器は切断扱い（スマホ・タブレットは切断中は隠れる）。
    private void UsbRead(string prefix, IReadOnlyList<BatteryDevice> list)
    {
        if (disposed) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in list) { seen.Add(device.Id); devices[device.Id] = device; }
        foreach (var (id, value) in devices.ToArray())
            if (id.StartsWith(prefix, StringComparison.Ordinal) && !seen.Contains(id) && value.Connected)
                devices[id] = value with { Connected = false, Reading = WithoutGuess(value.Reading) };
        Render();
    }
    private void AirPodsChanged(AirPodsReading value)
    {
        if (disposed || !settings.AirPods || AirPods.ToReading(value) is not { } reading) return;
        foreach (var (id, device) in devices.ToArray())
            if (device.Connected && id.StartsWith("bluetooth:", StringComparison.Ordinal) && BluetoothBatteryReader.IsAirPods(device.Name))
                devices[id] = device with { Reading = reading, UpdatedAt = DateTime.UtcNow };
        Render();
    }
    private void PollBluetooth()
    {
        if (disposed || Interlocked.Exchange(ref polling, 1) != 0) return;
        long until = Volatile.Read(ref burstUntil);   // 読み直しの続く時間が過ぎたら、ふだんの間隔に戻す
        if (until != 0 && Environment.TickCount64 > until && Interlocked.CompareExchange(ref burstUntil, 0, until) == until)
            bluetoothTimer.Change(BluetoothPeriod(), BluetoothPeriod());
        long version = Interlocked.Read(ref generation);
        try
        {
            var samples = BluetoothBatteryReader.Read();
            post(() =>
            {
                if (disposed || version != Interlocked.Read(ref generation) || !settings.Enabled || !settings.Bluetooth) return;
                var seen = new HashSet<string>();
                foreach (var sample in samples)
                {
                    var value = sample;
                    seen.Add(value.Id);
                    // AirPods は電池の値なしで届くので、電波から読んだ前の値を引き継ぐ。
                    if (value.Reading is null && devices.TryGetValue(value.Id, out var last)) value = value with { Reading = last.Reading, UpdatedAt = last.UpdatedAt };
                    // Sony のヘッドホンに直接聞いた値があれば、それを使う（残量も充電の状態も本物）。
                    else if (sample.Reading is not null && settings.SonyDirect && sony.Latest(value.Id) is { } direct)
                    {
                        // 推測の基準（前の残量）も最新にしておく。直接聞けなくなって Windows の値に戻ったとき、古い基準と比べて充電中と見間違えないため。
                        trend.Update(value.Id, direct.Percent, value.Connected, DateTime.UtcNow);
                        value = value with { Reading = direct };
                    }
                    // Windows から読んだ残量（充電中かどうかは分からない）は、残量の増え方から充電中かを推測する（BatteryTrend.cs）。
                    else if (sample.Reading is { Charge: BatteryCharge.Unknown } windows)
                    {
                        var charge = trend.Update(value.Id, windows.Percent, value.Connected, DateTime.UtcNow);
                        if (charge != BatteryCharge.Unknown) value = value with { Reading = windows with { Charge = charge, Detail = BatteryTrend.Note } };
                    }
                    devices[value.Id] = !value.Connected && devices.TryGetValue(value.Id, out var old)
                        ? value with { Reading = WithoutGuess(old.Reading), UpdatedAt = old.UpdatedAt } : value;
                }
                foreach (var (id, value) in devices.ToArray())
                    if (id.StartsWith("bluetooth:", StringComparison.Ordinal) && !seen.Contains(id))
                    {
                        trend.Update(id, 0, connected: false, DateTime.UtcNow);
                        devices[id] = value with { Connected = false, Reading = WithoutGuess(value.Reading) };
                    }
                UpdateAirPodsWatcher();
                if (settings.SonyDirect) sony.Consider(devices.Values.Where(IsSonyCandidate));
                Render();
            });
        }
        catch (Exception ex) { if (!disposed) log.Write($"Bluetooth 電池の読取: {ex.Message}"); }
        finally { Volatile.Write(ref polling, 0); }
    }
    // 切断した Bluetooth の機器は、充電の状態を外す（切断中は充電しているか分からない。直接聞いた値も、推測も同じ）。
    // 推測の断り書きも消す。AirPods の左右・ケースの内訳は残す。
    private static BatteryReading? WithoutGuess(BatteryReading? reading) => reading is null ? null :
        reading with { Charge = BatteryCharge.Unknown, ExternalPower = false, Detail = reading.Detail == BatteryTrend.Note ? null : reading.Detail };
    private static int IconSize()
    {
        nint taskbar = Native.FindWindow("Shell_TrayWnd", null);
        uint dpi = taskbar == 0 ? 0 : Native.GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = Native.GetDpiForSystem();
        int size = Native.GetSystemMetricsForDpi(49, dpi);
        return size > 0 ? size : 16;
    }
    private void Render()
    {
        // 通知領域で並べ替え中は描き替えない。5 秒ごとのテーマの確認で、離した後に描き直される。
        if (disposed || TrayInteraction.Busy) return;
        var assigned = BatteryLayout.Assign(devices.Values, settings);
        int size = IconSize();
        bool spinning = false;
        for (int i = 0; i < pool.Length; i++)
        {
            var slot = pool[i];
            if (assigned[i] is not { } device) { slot.Show(false); slot.Key = null; continue; }
            slot.Target.Tag = device.Name;
            var r = device.Reading;
            // 輪の色は残量だけで決める：普段は緑、lowWarn 以下はオレンジ、lowHot 以下は赤（BatteryMath.RingColor）。
            // 充電中かどうかは、右下の黄色の稲妻の有無で見せる（11/20 ユーザーの指定）。
            var (cr, cg, cb) = BatteryMath.RingColor(r?.Percent, settings.LowWarn, settings.LowHot);
            Color color = Color.FromArgb(cr, cg, cb);
            bool bolt = BatteryMath.ShowsBolt(r);
            // 残量 100% なら緑の代わりに虹の輪（11/20 ユーザーの指定）。つながっている間だけ回し、切断中（薄い表示）は止める。
            int step = -1;
            if (settings.RainbowWhenFull && r?.Percent >= 100)
            {
                step = device.Connected ? RainbowStep() : 0;
                spinning |= device.Connected;
            }
            var key = new DrawingKey(device.Kind, r?.Percent, color.ToArgb(), size, device.Connected, bolt, theme.IsLight, step);
            if (slot.Key != key)
            {
                if (step >= 0) slot.Reset(RainbowFrame(key, device, color, size, bolt));   // 取っておいたアイコンをそのまま出す（枠は持ち主にならない）
                else { using var bitmap = Draw(device, color, size, bolt, null); slot.Replace(bitmap); }
                slot.Key = key;
            }
            // マウスの枠（0）の説明には、肩代わりの状態も 2 行目に出す（11/20 ユーザーの指定）。
            slot.SetTooltip(i == 0 ? BatteryMath.Tooltip(device, statusText) : BatteryMath.Tooltip(device));
            slot.Show(true);
        }
        if (rainbowTimer.Enabled != spinning) rainbowTimer.Enabled = spinning;
    }
    // 11/20 ユーザーの指定：iPhone の電池ウィジェット風。残量の分だけ色の輪（減った部分は描かない）。中に白い機器の絵。
    // 数字はアイコンに描かず、マウスを乗せたときの説明に出す。
    // 輪はどれも滑らか。絵はマウス・キーボードがくっきり（点を 0/1 に分ける）、ヘッドホン・イヤホンが滑らか。
    private Bitmap Draw(BatteryDevice device, Color color, int size, bool bolt, double? rainbowHue)
    {
        var bitmap = BatteryIcon.RingIcon(device.Kind, device.Reading?.Percent, color, size,
            crispPicture: device.Kind is not (BatteryDeviceKind.Headphones or BatteryDeviceKind.Earbuds), light: theme.IsLight, rainbowHue: rainbowHue);
        if (bolt) BatteryIcon.StampBolt(bitmap, size, theme.IsLight);
        if (!device.Connected) BatteryIcon.Fade(bitmap, 0.4);
        return bitmap;
    }
    // 今の虹のコマ（0〜15）。1 周 RainbowLapMs で回る。
    private int RainbowStep() => (int)(rainbowClock.ElapsedMilliseconds * RainbowSteps / settings.RainbowLapMs % RainbowSteps);
    // 虹のコマは一度アイコンにしたら取っておく（回している間、同じ 16 枚を使い回す）。
    // 枠に出しているアイコンを途中で消さないよう、捨てるのはアプリを終えるときだけ。数は「種類×稲妻×明暗×大きさ×16」までに収まる。
    private Icon RainbowFrame(DrawingKey key, BatteryDevice device, Color color, int size, bool bolt)
    {
        if (rainbowFrames.TryGetValue(key, out var cached)) return cached.Icon;
        using var bitmap = Draw(device, color, size, bolt, key.Rainbow * 360.0 / RainbowSteps);
        nint handle = bitmap.GetHicon();
        var icon = Icon.FromHandle(handle);
        rainbowFrames.Add(key, (icon, handle));
        return icon;
    }
    private void ClearRainbowFrames()
    {
        foreach (var (icon, handle) in rainbowFrames.Values) { icon.Dispose(); TrayIconNative.DestroyIcon(handle); }
        rainbowFrames.Clear();
    }
    // Rainbow：虹のコマの番号（虹でないときは -1）。
    private sealed record DrawingKey(BatteryDeviceKind Kind, int? Percent, int Color, int Size, bool Connected, bool Bolt, bool Light, int Rainbow);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Interlocked.Increment(ref generation);
        theme.Changed -= Render;
        gap.BatteryChanged -= LogitechChanged;
        bluetoothTimer.Dispose(); themeTimer.Stop(); themeTimer.Dispose();
        rainbowTimer.Stop(); rainbowTimer.Dispose(); airPods.Dispose(); connections.Dispose(); sony.Dispose(); apple.Dispose(); portable.Dispose(); sonyTimer.Stop(); sonyTimer.Dispose();
        foreach (var slot in pool) slot.Dispose();
        ClearRainbowFrames();
    }
    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string className, string? title);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
        [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
    }
}
