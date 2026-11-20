using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using LogiGap.Core;
using Microsoft.Win32;

namespace LogiGap;

internal sealed class BatteryModule : IFeatureModule
{
    private readonly ILog log;
    private readonly GapModule gap;
    private readonly ContextMenuStrip menu;
    private readonly Action<Action> post;
    private readonly Dictionary<string, BatteryDevice> devices = [];
    private readonly IconSlot[] pool;   // 電池のアイコンの枠（最大 6 台）。左から 2 番目以降（オウム 0、温度・メモリ 1 の右）
    private readonly System.Threading.Timer bluetoothTimer;
    private readonly System.Windows.Forms.Timer themeTimer = new() { Interval = 5000 };
    private BatterySettings settings = new();
    private string? statusText;
    private long generation;
    private int polling;
    private volatile bool disposed;

    public BatteryModule(AppSettings settings, ILog log, GapModule gap, ContextMenuStrip menu, Action<Action> post, TrayOrder order)
    {
        this.log = log; this.gap = gap; this.menu = menu; this.post = post;
        // 番号の表は TrayIds。MonitorModule が 10 まで確保するので、電池は従来どおり 11〜16。
        pool = Enumerable.Range(0, TrayIds.BatteryCount).Select(i => new IconSlot(menu, order, 2 + i)).ToArray();
        for (int i = 0; i < pool.Length; i++) TrayIds.Check(pool[i], TrayIds.BatteryFirst + i, log);
        bluetoothTimer = new(_ => PollBluetooth(), null, Timeout.Infinite, Timeout.Infinite);
        gap.BatteryChanged += LogitechChanged;
        foreach (var value in gap.Batteries) devices[value.Id] = value;
        // 5 秒ごとに描き直す（通知領域で並べ替え中に描き替えを見送った分を、離した後に反映するため）。
        // 11/20：輪と絵の色はテーマに関係なく決まるので、テーマの読み取りはやめた。
        themeTimer.Tick += (_, _) => Render();
        SettingsChanged(settings); themeTimer.Start();
    }
    public IEnumerable<ToolStripItem> CreateMenuItems() => [];
    // LogiGap の状態（「LogiGap: 肩代わり中」など）。TrayContext が UI スレッドで渡す。マウスのアイコンの説明に出す。
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
                        ? old with { Connected = false } : value;
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
        // 枠は機器の種類ごとに固定：0 = マウス、1 = キーボード、2 = ヘッドホン、3〜5 = その他（2 台目以降のマウスなども）。
        // Windows はアイコンを「アプリ＋番号」で覚え、設定で表示にした順番で並べるので、種類と番号の対応を変えない。
        // ヘッドホンは、切断されたら薄く出さずに隠す（11/19 ユーザーの指定）。マウス・キーボードは眠るとすぐ切れるので薄く出す。
        var visible = devices.Values.Where(d => d.Reading != null && settings.Enabled &&
            (d.Id.StartsWith("logitech:", StringComparison.Ordinal) ? settings.Logitech : settings.Bluetooth) &&
            !settings.Hidden.Contains(d.Name, StringComparer.Ordinal) &&
            !(d.Kind == BatteryDeviceKind.Headphones && !d.Connected))
            .OrderBy(d => KindOrder(d.Kind)).ThenBy(d => d.Name, StringComparer.Ordinal).ToArray();
        var assigned = new BatteryDevice?[pool.Length];
        int spare = 3;
        foreach (var device in visible)
        {
            int index = KindOrder(device.Kind);
            if (index < 3 && assigned[index] is null) assigned[index] = device;
            else if (spare < pool.Length) assigned[spare++] = device;
        }
        int size = IconSize();
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
            var key = new DrawingKey(device.Kind, r?.Percent, color.ToArgb(), size, device.Connected, bolt);
            if (slot.Key != key)
            {
                // 11/20 ユーザーの指定：iPhone の電池ウィジェット風。残量の分だけ色の輪（減った部分は描かない）。中に白い機器の絵。
                // 数字はアイコンに描かず、マウスを乗せたときの説明に出す。
                // 輪はどれも滑らか。絵はマウス・キーボードがくっきり（点を 0/1 に分ける）、ヘッドホンが滑らか。
                using var bitmap = RingIcon(device.Kind, r?.Percent, color, size, crispPicture: device.Kind != BatteryDeviceKind.Headphones);
                if (bolt) StampBolt(bitmap, size);
                if (!device.Connected) Fade(bitmap, 0.4);
                slot.Replace(bitmap); slot.Key = key;
            }
            // マウスの枠（0）の説明には、LogiGap の状態も 2 行目に出す（11/20 ユーザーの指定）。
            slot.SetTooltip(i == 0 ? BatteryMath.Tooltip(device, statusText) : BatteryMath.Tooltip(device));
            slot.Show(true);
        }
    }
    private static int KindOrder(BatteryDeviceKind kind) => kind switch
    {
        BatteryDeviceKind.Mouse => 0, BatteryDeviceKind.Keyboard => 1, BatteryDeviceKind.Headphones => 2, _ => 3
    };
    // 輪と絵を別々に 4 倍の大きさで描いてから縮め、輪の上に絵を重ねる（Windows の記号と同じく縁が滑らかになる）。
    // 輪：どの機器も滑らか（11/20 ユーザーの指定。見本 5 の「ぼかさない縮め方」も試したうえで見本 3 に決定）。
    //   縁が半分透けて黒い背景と混ざり、色がくすんで見えるので、輪の色を明るくする（緑は色合いを変えずに一番明るく）。
    // 絵：crispPicture（マウス・キーボード）なら縮めた後で点を 0/1 に分けてくっきりさせる。ヘッドホンは滑らかのまま。
    private static Bitmap RingIcon(BatteryDeviceKind kind, int? percent, Color color, int size, bool crispPicture)
    {
        const int k = 4; int s = size * k;
        Bitmap icon;
        using (var ringBig = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(ringBig)) DrawRing(g, s, percent, Brighten(color));
            icon = Shrink(ringBig, size);
        }
        using var pictureBig = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(pictureBig)) DrawPicture(g, s, kind);
        using var picture = Shrink(pictureBig, size);
        if (crispPicture)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = picture.GetPixel(x, y);
                    picture.SetPixel(x, y, c.A >= 128 ? Color.FromArgb(255, c) : Color.Transparent);
                }
        using (var g = Graphics.FromImage(icon))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.DrawImageUnscaled(picture, 0, 0);
        }
        return icon;
    }
    // 20px のアイコンで太さ 2 ドットの輪（s は描く大きさ）。
    // 11/20 ユーザーの指定（見本 B）：輪をアイコンの端まで 1 ドット広げる。くっきりにしたときの見栄えがよくなる。
    // 11/20 ユーザーの指定：減った部分（灰色の輪）は描かない。数字を出さない時点で正確さより見栄えを優先する。
    private static void DrawRing(Graphics g, int s, int? percent, Color color)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        float thick = s / 10f, inset = thick / 2f;
        var ring = new RectangleF(inset, inset, s - inset * 2, s - inset * 2);
        if (percent is int p && p > 0)
            using (var arc = new Pen(color, thick) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawArc(arc, ring, -90, 360f * Math.Clamp(p, 0, 100) / 100f);
    }
    // 輪の内側に白い機器の絵（s は描く大きさ）。
    private static void DrawPicture(Graphics g, int s, BatteryDeviceKind kind)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        float thick = s / 10f, inner = s - 2 * (thick + s / 10f);
        DevicePicture(g, kind, new RectangleF((s - inner) / 2, (s - inner) / 2, inner, inner), Color.White);
    }
    // 滑らかに縮める（少しぼける）。
    private static Bitmap Shrink(Bitmap big, int size)
    {
        var icon = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(icon);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(big, new Rectangle(0, 0, size, size));
        return icon;
    }
    // 充電中の印：右下に黄色の稲妻（11/20 ユーザーの指定。「充電は黄色や黄色っぽいオレンジのイメージ」）。
    // 稲妻は 4 倍で描いて縮め、点を 0/1 に分けてくっきりさせる。まわり 1 ドットは透明にして、輪や絵と離す。
    // 置き場所は 20px のアイコンで横 13.5〜20・縦 11〜20 の箱（大きさに合わせて伸び縮みする）。
    private static readonly Color BoltColor = Color.FromArgb(255, 214, 10);
    private static void StampBolt(Bitmap icon, int size)
    {
        const int k = 4; int s = size * k; float unit = size / 20f;
        var box = new RectangleF(13.5f * unit, 11f * unit, 6.5f * unit, 9f * unit);
        // 稲妻の形（箱の中の割合）：上の辺 → 右上の帯を左下へ → 真ん中で右へ張り出す → 下の先へ → 左の帯を戻る。
        var shape = new (float X, float Y)[] { (0.50f, 0f), (0.88f, 0f), (0.62f, 0.40f), (1.0f, 0.40f), (0.22f, 1f), (0.42f, 0.56f), (0.02f, 0.56f) };
        bool[,] mask = new bool[size, size];
        using (var big = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(big))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.FillPolygon(Brushes.White, shape.Select(p => new PointF((box.X + p.X * box.Width) * k, (box.Y + p.Y * box.Height) * k)).ToArray());
            }
            using var small = Shrink(big, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) mask[y, x] = small.GetPixel(x, y).A >= 110;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1 && !near; dx++)
                    {
                        int yy = y + dy, xx = x + dx;
                        near = yy >= 0 && yy < size && xx >= 0 && xx < size && mask[yy, xx];
                    }
                if (near) icon.SetPixel(x, y, mask[y, x] ? BoltColor : Color.Transparent);
            }
    }
    // 色合いを変えずに一番明るい所まで上げる（緑 199 → 255）。黄色と赤はもう一番明るいので変わらない。
    private static Color Brighten(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        if (max == 0) return c;
        double f = 255.0 / max;
        return Color.FromArgb(c.A, (int)Math.Min(255, Math.Round(c.R * f)), (int)Math.Min(255, Math.Round(c.G * f)), (int)Math.Min(255, Math.Round(c.B * f)));
    }
    // 機器の絵を自分で描く（11/20 ユーザー：マウスの真ん中の線を残す、ヘッドホンの耳当てを丸く）。穴は透明で抜く。
    private static void DevicePicture(Graphics g, BatteryDeviceKind kind, RectangleF a, Color color)
    {
        using var brush = new SolidBrush(color);
        using var clear = new SolidBrush(Color.Transparent);
        var mode = g.CompositingMode;
        switch (kind)
        {
            case BatteryDeviceKind.Mouse:
            {
                float w = a.Width * 0.64f;
                using (var body = Rounded(new RectangleF(a.X + (a.Width - w) / 2, a.Y, w, a.Height), w / 2)) g.FillPath(brush, body);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                float line = Math.Max(1f, a.Width / 14f);   // 左右のボタンの境目（上から 4 割）
                g.FillRectangle(clear, a.X + a.Width / 2 - line / 2, a.Y, line, a.Height * 0.42f);
                break;
            }
            case BatteryDeviceKind.Keyboard:
            {
                float h = a.Height * 0.66f; var body = new RectangleF(a.X, a.Y + (a.Height - h) / 2, a.Width, h);
                using (var path = Rounded(body, a.Width * 0.14f)) g.FillPath(brush, path);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                float unit = a.Width / 14f, key = Math.Max(1f, unit * 1.2f);
                for (int i = 0; i < 5; i++) g.FillRectangle(clear, body.X + unit * (2 + i * 2.2f), body.Y + body.Height * 0.2f, key, key);
                for (int i = 0; i < 4; i++) g.FillRectangle(clear, body.X + unit * (3.1f + i * 2.2f), body.Y + body.Height * 0.45f, key, key);
                g.FillRectangle(clear, body.X + body.Width * 0.28f, body.Y + body.Height * 0.7f, body.Width * 0.44f, key);   // スペースキー
                break;
            }
            case BatteryDeviceKind.Headphones:
            {
                // iPhone の記号のように、ヘッドバンドと耳当ての外側を 1 つの円に沿わせる（11/20 ユーザー：耳当てが輪にかかって見える）。
                // ヘッドバンドは円の上半分から両脇の少し下まで。耳当ては、円の中に収まるよう円で切り取った縦長の形。
                // 見本で 5 通り比べて決めた値：線は絵の幅の 1/5、耳当ての幅は 0.40、絵の高さの 44% の所から下まで。
                float pen = Math.Max(1.5f, a.Width / 5f);
                var circle = new RectangleF(a.X + pen / 2, a.Y + pen / 2, a.Width - pen, a.Height - pen);
                using (var band = new Pen(color, pen)) g.DrawArc(band, circle, 150, 240);
                float cupWidth = a.Width * 0.40f, cupTop = a.Y + a.Height * 0.44f, cupBottom = a.Bottom;
                using var disk = new System.Drawing.Drawing2D.GraphicsPath();
                disk.AddEllipse(a);
                var clip = g.Clip;
                g.SetClip(disk);
                // 円で切るので外側は円に沿って曲がる。内側の角だけ丸くする。
                using (var left = Rounded(new RectangleF(a.X - cupWidth, cupTop, cupWidth * 2, cupBottom - cupTop), cupWidth * 0.5f)) g.FillPath(brush, left);
                using (var right = Rounded(new RectangleF(a.Right - cupWidth, cupTop, cupWidth * 2, cupBottom - cupTop), cupWidth * 0.5f)) g.FillPath(brush, right);
                g.Clip = clip;
                break;
            }
            default:
                g.FillEllipse(brush, a.X + a.Width * 0.2f, a.Y + a.Height * 0.2f, a.Width * 0.6f, a.Height * 0.6f);
                break;
        }
        g.CompositingMode = mode;
    }
    private static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    // 切断中は全体を薄くする（不透明度を factor 倍）。
    private static void Fade(Bitmap bitmap, double factor)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.A > 0) bitmap.SetPixel(x, y, Color.FromArgb((int)(p.A * factor), p));
            }
    }
    private sealed record DrawingKey(BatteryDeviceKind Kind, int? Percent, int Color, int Size, bool Connected, bool Bolt);
    // 電池のアイコンの枠。起動時に決まった数・決まった順番で作り、機器を種類の順に割り当てる。
    // Windows はアイコンを「アプリ＋番号」で覚えるので、起動のたびに番号と機器の対応が入れ替わらないようにする。
    private sealed class IconSlot : IDisposable
    {
        private readonly NotifyIcon target;
        private readonly TrayOrder order;
        private nint handle;
        private Icon? wrapper;
        public IconSlot(ContextMenuStrip menu, TrayOrder order, int rank)
        {
            target = new() { ContextMenuStrip = menu }; this.order = order;
            order.Register(target, rank);
        }
        public DrawingKey? Key { get; set; }
        public void SetTooltip(string text) { if (target.Text != text) target.Text = text; }
        public void Show(bool show) { if (show && target.Icon is null) return; order.Want(target, show); }
        public void Replace(Bitmap bitmap)
        {
            nint nextHandle = bitmap.GetHicon();
            Icon? next = null;
            try { next = Icon.FromHandle(nextHandle); target.Icon = next; }
            catch { next?.Dispose(); Native.DestroyIcon(nextHandle); throw; }
            Release(); handle = nextHandle; wrapper = next;
        }
        private void Release()
        {
            wrapper?.Dispose(); wrapper = null;
            if (handle != 0) { Native.DestroyIcon(handle); handle = 0; }
        }
        public void Dispose()
        {
            target.Visible = false; target.Icon = null; target.ContextMenuStrip = null;
            target.Dispose(); Release();
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Interlocked.Increment(ref generation);
        gap.BatteryChanged -= LogitechChanged;
        bluetoothTimer.Dispose(); themeTimer.Stop(); themeTimer.Dispose();
        foreach (var slot in pool) slot.Dispose();
    }
    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string className, string? title);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
        [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyIcon(nint icon);
    }
}

internal static class BluetoothBatteryReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    private static readonly PropertyKey BatteryKey = new(new("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly PropertyKey FriendlyNameKey = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static IReadOnlyList<BatteryDevice> Read()
    {
        nint set = SetupDiGetClassDevsW(0, null, 0, 2 | 4);
        if (set == -1) throw new Win32Exception();
        var result = new List<BatteryDevice>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var battery = Property(set, ref info, BatteryKey, 3); // DEVPROP_TYPE_BYTE
                if (battery is not { Length: 1 } || battery[0] > 100) continue;
                var nameBytes = Property(set, ref info, FriendlyNameKey, 0x12); // DEVPROP_TYPE_STRING
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _)) continue;
                // 要確認: 名前を取れない機器は PnP インスタンス ID で表示。識別子もこれを使い、同名の別機器を混同しない。
                string name = nameBytes == null ? id.ToString() : BatteryMath.BluetoothName(Encoding.Unicode.GetString(nameBytes).TrimEnd('\0'));
                // 要確認: DN_STARTED が実機の Bluetooth 切断時に落ちるかは未確認。列挙から消えた機器も UI 側で切断表示する。
                bool connected = CM_Get_DevNode_Status(out uint status, out _, info.DevInst, 0) == 0 && (status & 8) != 0;
                result.Add(new($"bluetooth:{id}", name, BatteryDeviceKind.Headphones, connected,
                    new(battery[0], BatteryLevel.Unknown, BatteryCharge.Unknown, false, false), DateTime.UtcNow));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
    private static byte[]? Property(nint set, ref DeviceInfo info, PropertyKey key, uint expectedType)
    {
        SetupDiGetDevicePropertyW(set, ref info, in key, out uint type, null, 0, out uint size, 0);
        if (Marshal.GetLastWin32Error() != 122 || size == 0 || size > 65536 || type != expectedType) return null;
        byte[] buffer = new byte[size];
        return SetupDiGetDevicePropertyW(set, ref info, in key, out type, buffer, size, out uint used, 0) &&
            type == expectedType && used == size ? buffer : null;
    }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevsW(nint classGuid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDevicePropertyW(nint set, ref DeviceInfo info,
        in PropertyKey key, out uint type, [Out] byte[]? buffer, uint size, out uint requiredSize, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInstanceIdW(nint set, ref DeviceInfo info,
        StringBuilder id, uint size, out uint requiredSize);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
