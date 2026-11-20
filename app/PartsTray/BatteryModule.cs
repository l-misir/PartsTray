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
    private BatterySettings settings = new();
    private string? statusText;
    private long generation;
    private int polling;
    private volatile bool disposed;

    public BatteryModule(AppSettings settings, ILog log, GapModule gap, ContextMenuStrip menu, Action<Action> post, TrayOrder order, TaskbarTheme theme)
    {
        this.theme = theme; theme.Changed += Render;
        this.log = log; this.gap = gap; this.menu = menu; this.post = post;
        // 番号の表は TrayIds。MonitorModule が 10 まで確保し、電池は 11〜22（従来の 11〜13 を維持）。
        pool = Enumerable.Range(0, TrayIds.BatteryCount).Select(i => new TrayIconSlot<DrawingKey>(menu, order, 2 + i)).ToArray();
        for (int i = 0; i < pool.Length; i++) TrayIds.Check(pool[i].Target, TrayIds.BatteryFirst + i, log);
        bluetoothTimer = new(_ => PollBluetooth(), null, Timeout.Infinite, Timeout.Infinite);
        gap.BatteryChanged += LogitechChanged;
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
        .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    public IReadOnlyList<BatteryDevice> GetDevices() => Array.AsReadOnly(devices.Values.Where(d => d.Reading != null)
        .OrderBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal).ToArray());
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
        Interlocked.Increment(ref generation);
        bluetoothTimer.Change(settings.Enabled && settings.Bluetooth ? 0 : Timeout.Infinite,
            settings.BluetoothPollSeconds * 1000);
        Render();
    }
    private void PollBluetooth()
    {
        if (disposed || Interlocked.Exchange(ref polling, 1) != 0) return;
        long version = Interlocked.Read(ref generation);
        try
        {
            var samples = BluetoothBatteryReader.Read();
            post(() =>
            {
                if (disposed || version != Interlocked.Read(ref generation) || !settings.Enabled || !settings.Bluetooth) return;
                var seen = new HashSet<string>();
                foreach (var value in samples)
                {
                    seen.Add(value.Id);
                    devices[value.Id] = !value.Connected && devices.TryGetValue(value.Id, out var old)
                        ? value with { Reading = old.Reading, UpdatedAt = old.UpdatedAt } : value;
                }
                foreach (var (id, value) in devices.ToArray())
                    if (id.StartsWith("bluetooth:", StringComparison.Ordinal) && !seen.Contains(id))
                        devices[id] = value with { Connected = false };
                Render();
            });
        }
        catch (Exception ex) { if (!disposed) log.Write($"Bluetooth 電池の読取: {ex.Message}"); }
        finally { Volatile.Write(ref polling, 0); }
    }
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
        rainbowTimer.Stop(); rainbowTimer.Dispose();
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
