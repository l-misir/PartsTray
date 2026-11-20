// Logi Bolt の機器の電池・名前・種類を読み取り専用で追跡します。
// 照会の順序・更新間隔・再接続の扱いを変える場所です。
using System.Text;

namespace LogiGap.Core;

/// <summary>HID の読み取りだけを行う追跡器。時計・送信先以外の OS 依存は持たない。</summary>
public sealed class LogitechBatteryTracker
{
    private enum Step { Ping, Unified, Legacy, NameFeature, NameLength, NameChunk, Type, Status, Idle }
    private sealed class Device(byte slot, string receiver)
    {
        public byte Slot = slot, BatteryFeature, NameFeature;
        public ushort BatteryId, Wpid;
        public bool Known, Blocked, Queued;
        public Step Step = Step.Idle;
        public int NameLength;
        public List<byte> Name = [];
        public TimeSpan RefreshedAt;
        public BatteryDevice Value = new($"logitech:{receiver}:{slot}", $"Logitech {slot}", BatteryDeviceKind.Other, false, null, null);
    }
    private sealed class Pending(Device device, byte[] frame, TimeSpan deadline)
    {
        public Device Device = device;
        public byte[] Frame = frame;
        public TimeSpan Deadline = deadline;
        public int Retries;
    }
    private readonly object gate = new();
    private readonly IReportTransport transport;
    private readonly IClock clock;
    private readonly Func<DateTime> utcNow;
    private readonly Dictionary<byte, Device> devices = [];
    private readonly Queue<Device> ready = [];
    private BatterySettings settings;
    private Pending? pending;
    private bool receiverAvailable = true;
    private string receiver = "";
    private bool Enabled => settings.Enabled && settings.Logitech;

    public LogitechBatteryTracker(IReportTransport transport, IClock clock, BatterySettings? settings = null, Func<DateTime>? utcNow = null)
    {
        this.transport = transport; this.clock = clock; this.settings = settings ?? new();
        this.settings.Validate(); this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }
    // ロック内から呼ぶ。購読側は UI へ投函するだけにし、Session には触れない。
    public event Action<BatteryDevice>? Changed;
    private bool supportedMouseSeen;
    public bool SupportedMouseSeen { get { lock (gate) return supportedMouseSeen; } }
    public IReadOnlyList<BatteryDevice> Snapshot { get { lock (gate) return devices.Values.Select(d => d.Value).ToArray(); } }

    private Device Get(byte slot)
    {
        if (!devices.TryGetValue(slot, out var d)) devices.Add(slot, d = new(slot, receiver));
        return d;
    }
    private void Update(Device d, BatteryDevice value)
    {
        if (d.Value == value) return;
        d.Value = value; Changed?.Invoke(value);
    }
    private void Queue(Device d)
    {
        if (d.Queued || d.Blocked || d.Step == Step.Idle) return;
        d.Queued = true; ready.Enqueue(d);
    }
    private void ClearWork()
    {
        pending = null; ready.Clear();
        foreach (var d in devices.Values) { d.Queued = false; d.Step = Step.Idle; }
    }
    public void ReceiverLost()
    {
        lock (gate)
        {
            receiverAvailable = false; ClearWork();
            foreach (var d in devices.Values) Update(d, d.Value with { Connected = false });
        }
    }
    public void ReceiverOpened(string identity = "")
    {
        lock (gate)
        {
            ReceiverLost();
            // 要確認: 別レシーバーの同じ番号に古い機能番号を流用しない。UI 側は以前の値を切断表示で保持する。
            if (receiver != identity) { devices.Clear(); receiver = identity; }
            receiverAvailable = true;
            if (Enabled) Probe();
        }
    }
    private void Probe()
    {
        for (byte slot = 1; slot <= 6; slot++)
        {
            var d = Get(slot); d.Blocked = false; d.Step = Step.Ping; Queue(d);
        }
        Pump();
    }
    public void UpdateSettings(BatterySettings value)
    {
        value.Validate();
        lock (gate)
        {
            bool wasEnabled = Enabled; settings = value;
            if (!Enabled)
            {
                ClearWork();
                foreach (var d in devices.Values) Update(d, d.Value with { Connected = false });
            }
            else if (!wasEnabled && receiverAvailable) Probe();
        }
    }
    private void Link(Device d)
    {
        if (pending?.Device == d) pending = null;
        d.Blocked = false;
        Update(d, d.Value with { Connected = true });
        if (!d.Known) { d.BatteryFeature = d.NameFeature = 0; d.BatteryId = 0; d.Name.Clear(); }
        d.Step = d.Known ? Step.Status : Step.Unified;
        Queue(d);
    }
    public void Receive(HidReport r)
    {
        lock (gate)
        {
            if (!Enabled || !receiverAvailable || r.Device is < 1 or > 6) return;
            var d = Get(r.Device);
            if (r.Kind == ReportKind.Connection)
            {
                if (r.Parameters.Length < 3) return;
                if (d.Wpid != 0 && r.Wpid != d.Wpid)
                {
                    // 要確認: 番号の再ペアリングは別機器として扱い、以前のアイコンは切断表示のまま残す。
                    Update(d, d.Value with { Connected = false });
                    d.Known = false;
                    d.Value = new($"logitech:{receiver}:{d.Slot}:{r.Wpid:X4}", $"Logitech {d.Slot}", BatteryDeviceKind.Other, false, null, null);
                }
                d.Wpid = r.Wpid;
                if (r.Wpid == 0xB042 && r.LinkUp) supportedMouseSeen = true;
                if (r.LinkUp) Link(d);
                else
                {
                    if (pending?.Device == d) pending = null;
                    d.Step = Step.Idle; Update(d, d.Value with { Connected = false });
                }
                Pump(); return;
            }
            if (r.Kind == ReportKind.Notification && r.Swid == 0 && r.Function == 0 &&
                d.BatteryFeature != 0 && r.Feature == d.BatteryFeature)
            {
                ReadBattery(d, r.Parameters); return;
            }
            var p = pending;
            if (p is null || p.Device != d || r.Swid != 7 || r.Feature != p.Frame[2] || r.Function != p.Frame[3] >> 4 ||
                r.Kind is not (ReportKind.Response or ReportKind.Error10 or ReportKind.Error20)) return;
            pending = null;
            Advance(d, r.Kind == ReportKind.Response ? r : null);
            Queue(d); Pump();
        }
    }
    private void Advance(Device d, HidReport? reply)
    {
        var p = reply?.Parameters ?? [];
        byte index = p.Length > 0 && p[0] < 0x80 ? p[0] : (byte)0;
        switch (d.Step)
        {
            case Step.Ping:
                if (p.Length >= 3 && p[2] == 0x7A) Link(d);
                else d.Step = Step.Idle;
                break;
            case Step.Unified:
                d.BatteryFeature = index; d.BatteryId = index == 0 ? (ushort)0 : (ushort)0x1004;
                d.Step = index == 0 ? Step.Legacy : Step.NameFeature; break;
            case Step.Legacy:
                d.BatteryFeature = index; d.BatteryId = index == 0 ? (ushort)0 : (ushort)0x1000;
                d.Step = Step.NameFeature; break;
            case Step.NameFeature:
                // 要確認: 重複した機能番号は不正な応答として無視し、機器番号を仮の名前にする。
                d.NameFeature = index == d.BatteryFeature ? (byte)0 : index;
                d.Step = d.NameFeature == 0 ? Step.Status : Step.NameLength; break;
            case Step.NameLength:
                d.NameLength = p.Length > 0 ? p[0] : 0; d.Name.Clear();
                d.Step = d.NameLength == 0 ? Step.Type : Step.NameChunk; break;
            case Step.NameChunk:
                if (p.Length == 0) { d.Step = Step.Type; break; }
                d.Name.AddRange(p.Take(Math.Min(d.NameLength - d.Name.Count, p.Length)));
                if (d.Name.Count == d.NameLength)
                {
                    string name = Encoding.UTF8.GetString(d.Name.ToArray()).TrimEnd('\0');
                    // 要確認: 起動時は WPID 通知がないため、従来の起動識別と同じ完全一致名を使う。照会は増やさない。
                    if (name == "MX Master 4") supportedMouseSeen = true;
                    if (!string.IsNullOrWhiteSpace(name)) Update(d, d.Value with { Name = name });
                    d.Step = Step.Type;
                }
                break;
            case Step.Type:
                // 要確認: DEVICE_NAME の種類は 0=キーボード、3=マウスだけを識別し、ほかはその他とする。
                var kind = p.Length == 0 ? BatteryDeviceKind.Other : p[0] switch
                { 0 => BatteryDeviceKind.Keyboard, 3 => BatteryDeviceKind.Mouse, _ => BatteryDeviceKind.Other };
                Update(d, d.Value with { Kind = kind }); d.Step = Step.Status; break;
            case Step.Status:
                ReadBattery(d, p); d.Step = Step.Idle; d.RefreshedAt = clock.Now; break;
        }
        if (d.Step == Step.Status) d.Known = true;
    }
    private void ReadBattery(Device d, ReadOnlySpan<byte> p)
    {
        if (BatteryMath.Decode(p, d.BatteryId == 0x1004) is { } reading)
            Update(d, d.Value with { Reading = reading, UpdatedAt = utcNow() });
    }

    public void Pump()
    {
        lock (gate)
        {
            if (!Enabled || !receiverAvailable) return;
            if (pending is { } p)
            {
                if (clock.Now < p.Deadline) return;
                if (p.Retries < 2)
                {
                    p.Retries++; p.Deadline = clock.Now + TimeSpan.FromSeconds(1); Submit(p); return;
                }
                p.Device.Blocked = true; p.Device.Step = Step.Idle; pending = null;
            }
            foreach (var d in devices.Values)
                if (d.Value.Connected && d.Known && !d.Blocked && d.Step == Step.Idle &&
                    clock.Now - d.RefreshedAt >= TimeSpan.FromMinutes(settings.RefreshMinutes))
                { d.Step = Step.Status; Queue(d); }
            while (pending == null && ready.TryDequeue(out var d))
            {
                d.Queued = false;
                if (d.Blocked || d.Step == Step.Idle || (!d.Value.Connected && d.Step != Step.Ping)) continue;
                if (d.Step == Step.Status && d.BatteryFeature == 0)
                { d.Step = Step.Idle; d.RefreshedAt = clock.Now; continue; }
                var (feature, fn, parameters) = Command(d);
                byte[] frame = HidReport.Request(d.Slot, feature, fn, parameters, swid: 7);
                var next = new Pending(d, frame, clock.Now + TimeSpan.FromSeconds(1));
                pending = next; Submit(next);
            }
        }
    }
    private static (byte Feature, byte Fn, byte[] Parameters) Command(Device d) => d.Step switch
    {
        Step.Ping => (0, 1, [0, 0, 0x7A]),
        Step.Unified => (0, 0, [0x10, 4]), Step.Legacy => (0, 0, [0x10, 0]),
        Step.NameFeature => (0, 0, [0, 5]),
        Step.NameLength => (d.NameFeature, 0, []), Step.NameChunk => (d.NameFeature, 1, [(byte)d.Name.Count]),
        Step.Type => (d.NameFeature, 2, []),
        Step.Status => (d.BatteryFeature, (byte)(d.BatteryId == 0x1004 ? 1 : 0), []),
        _ => throw new InvalidOperationException("電池の照会段階が不正です")
    };
    private void Submit(Pending p)
    {
        ValidateRequest(p.Frame);
        try { transport.Send(p.Frame); }
        catch { pending = null; p.Device.Blocked = true; p.Device.Step = Step.Idle; throw; }
    }
    public void ValidateRequest(byte[] raw)
    {
        lock (gate)
        {
            var r = HidReport.Parse(raw);
            if (raw.Length != 20 || raw[0] != 0x11 || r is null || r.Swid != 7 ||
                r.Device is < 1 or > 6 || r.Kind != ReportKind.Response)
                throw new InvalidOperationException("電池の出力形式を拒否");
            var p = r.Parameters;
            bool ZeroFrom(int start) => p.Skip(start).All(b => b == 0);
            bool allowed = r.Feature == 0 &&
                ((r.Function == 0 && HidReport.U16(p) is 0x0005 or 0x1004 or 0x1000 && ZeroFrom(2)) ||
                 (r.Function == 1 && p[0] == 0 && p[1] == 0 && p[2] == 0x7A && ZeroFrom(3)));
            if (devices.TryGetValue(r.Device, out var d))
            {
                allowed |= d.NameFeature != 0 && r.Feature == d.NameFeature &&
                    ((r.Function is 0 or 2 && ZeroFrom(0)) ||
                     (r.Function == 1 && p[0] < d.NameLength && ZeroFrom(1)));
                allowed |= d.BatteryFeature != 0 && r.Feature == d.BatteryFeature && ZeroFrom(0) &&
                    ((d.BatteryId == 0x1004 && r.Function == 1) || (d.BatteryId == 0x1000 && r.Function == 0));
            }
            if (!allowed) throw new InvalidOperationException("電池の許可リスト外の要求を拒否");
        }
    }
}
