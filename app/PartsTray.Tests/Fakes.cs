using PartsTray.Core;

namespace PartsTray.Tests;

internal sealed class FakeClock : IClock { public TimeSpan Now { get; set; } }
internal sealed class FakeInput : IInputSink
{
    public List<string> KeyEvents { get; } = [];
    public List<int> WheelEvents { get; } = [];
    public bool ThrowOnWheel { get; set; }
    public void Keys(string keys) => KeyEvents.Add(keys);
    public void HorizontalWheel(int delta)
    {
        if (ThrowOnWheel) throw new InvalidOperationException("fake wheel failure");
        WheelEvents.Add(delta);
    }
}
internal sealed class FakeLog : ILog { public List<string> Lines { get; } = []; public void Write(string message) => Lines.Add(message); }
internal sealed class Harness : IReportTransport
{
    public FakeClock Clock { get; } = new();
    public FakeInput Input { get; } = new();
    public FakeLog Log { get; } = new();
    public GapSession Session { get; }
    public List<byte[]> Sent { get; } = [];
    public Dictionary<ushort, byte> Features { get; } = new() { [0x2201] = 20, [0x1B04] = 13, [0x2150] = 19, [0x1D4B] = 4 };
    public Dictionary<ushort, byte> Cids { get; } = [];
    public byte Thumb { get; set; }
    public Func<byte[], bool>? Intercept { get; set; }
    public bool AutoReply { get; set; } = true;
    public bool OptionsRunning { get; set; } = true;
    public DateTime UtcNow { get; set; } = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
    public DateTime? OptionsStartedUtc { get; set; }
    public Dictionary<byte, string> Names { get; } = [];
    public Dictionary<byte, byte> NameFeatures { get; } = [];
    public bool ShortNameReplies { get; set; }
    // 指示書 N: 本番の既定は無効。肩代わりの既存テストでは有効な利用者設定を明示する。
    public Harness(AppSettings? config = null) => Session = new(this, Input, Clock, Log, () => OptionsRunning, config ?? new() { GapFiller = new() { Enabled = true } },
        optionsStartedUtc: () => OptionsStartedUtc, utcNow: () => UtcNow);
    public void Send(byte[] report)
    {
        Sent.Add(report.ToArray());
        if (Intercept?.Invoke(report) == true || !AutoReply) return;
        Reply(report);
    }
    public void Reply(byte[] report)
    {
        var r = HidReport.Parse(report)!;
        byte[] p = r.Parameters.ToArray();
        if (r.Feature == 0 && r.Function == 0) p = [Features.GetValueOrDefault(HidReport.U16(p)), 0, 1];
        if (r.Feature == 0 && r.Function == 1) p = [4, 5, 0x5A];
        byte nameFeature = NameFeatures.GetValueOrDefault(r.Device, (byte)3);
        if (r.Feature == 0 && r.Function == 0 && HidReport.U16(r.Parameters) == 5)
            p = [Names.ContainsKey(r.Device) ? nameFeature : (byte)0, 0, 1];
        if (r.Feature == nameFeature && Names.TryGetValue(r.Device, out string? name))
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name);
            if (r.Function == 0) p = [(byte)bytes.Length];
            if (r.Function == 1)
            {
                p = bytes.Skip(r.Parameters[0]).Take(ShortNameReplies ? 3 : 16).ToArray();
                if (ShortNameReplies)
                {
                    byte[] raw = new byte[7]; raw[0] = 0x10; raw[1] = r.Device; raw[2] = nameFeature; raw[3] = 0x15;
                    p.CopyTo(raw, 4); Session.Receive(raw); return;
                }
            }
        }
        if (r.Feature == Features[0x1B04] && r.Function == 3) Cids[HidReport.U16(p)] = (byte)(p[2] & 1);
        if (r.Feature == Features[0x1B04] && r.Function == 2) p[2] = Cids.GetValueOrDefault(HidReport.U16(p));
        if (r.Feature == Features[0x2150] && r.Function == 2) { Thumb = p[0]; p = [0, 0]; }
        if (r.Feature == Features[0x2150] && r.Function == 1) p = [Thumb, Thumb];
        Session.Receive(Frame(r.Device, r.Feature, r.Function, 5, p));
    }
    public static byte[] Frame(byte dev, byte feature, byte fn, byte swid, params byte[] p)
    {
        var raw = new byte[20]; raw[0] = 0x11; raw[1] = dev; raw[2] = feature; raw[3] = (byte)(fn << 4 | swid);
        p.CopyTo(raw, 4); return raw;
    }
    public void Link(byte dev = 2) => Session.Receive([0x10, dev, 0x41, 0x10, 2, 0x42, 0xB0]);
    public void Activate() { Link(); Session.Pump(); }
    public void Options(byte feature = 20, byte fn = 3, params byte[] p) => Session.Receive(Frame(2, feature, fn, 14, p));
    public void OptionsFrom(byte swid, byte feature = 20, byte fn = 3, params byte[] p) => Session.Receive(Frame(2, feature, fn, swid, p));
    public void Buttons(params ushort[] cids)
    {
        byte[] p = new byte[8];
        for (int i = 0; i < cids.Length; i++) { p[i * 2] = (byte)(cids[i] >> 8); p[i * 2 + 1] = (byte)cids[i]; }
        Session.Receive(Frame(2, Features[0x1B04], 0, 0, p));
    }
    public byte[][] Writes => Sent.Where(r => (r[2] == Features[0x2201] || r[2] == Features[0x1B04]) && r[3] == 0x35 || r[2] == Features[0x2150] && r[3] == 0x25).ToArray();
    public void Advance(double seconds) { Clock.Now += TimeSpan.FromSeconds(seconds); Session.Pump(); }
}
