using System.Text;
using System.Text.Json;
using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class BatteryTests
{
    private sealed class Rig : IReportTransport
    {
        public FakeClock Clock { get; } = new();
        public List<byte[]> Sent { get; } = [];
        public List<BatteryDevice> Changes { get; } = [];
        public LogitechBatteryTracker Tracker { get; }
        public DateTime Utc => new DateTime(2026, 11, 19, 13, 31, 0, DateTimeKind.Utc) + Clock.Now;
        public Rig(BatterySettings? settings = null)
        {
            Tracker = new(this, Clock, settings, () => Utc);
            Tracker.Changed += Changes.Add;
        }
        public void Send(byte[] report) => Sent.Add(report.ToArray());
        public HidReport Last => HidReport.Parse(Sent.Last())!;
        public BatteryDevice Device(byte slot = 2) => Tracker.Snapshot.Single(d => d.Id.EndsWith($":{slot}", StringComparison.Ordinal));
        public void Receive(byte[] raw) => Tracker.Receive(HidReport.Parse(raw)!);
        public void Link(byte slot = 2, bool up = true) => Receive([0x10, slot, 0x41, 0x10, (byte)(up ? 2 : 0x42), 0x42, 0xB0]);
        public void Reply(params byte[] p) => Receive(Harness.Frame(Last.Device, Last.Feature, Last.Function, 7, p));
        public void Error(bool shortReport = false)
        {
            byte[] raw = new byte[shortReport ? 7 : 20];
            raw[0] = shortReport ? (byte)0x10 : (byte)0x11; raw[1] = Last.Device;
            raw[2] = shortReport ? (byte)0x8F : (byte)0xFF; raw[3] = Last.Feature;
            raw[4] = (byte)(Last.Function << 4 | 7); raw[5] = 2; Receive(raw);
        }
        public void Advance(double seconds) { Clock.Now += TimeSpan.FromSeconds(seconds); Tracker.Pump(); }
        public void Discover(string name = "MX Master 4", bool legacy = false, byte type = 3)
        {
            Link(); Reply(legacy ? (byte)0 : (byte)9);
            if (legacy) Reply(8);
            Reply(3);
            byte[] bytes = Encoding.UTF8.GetBytes(name);
            Reply((byte)bytes.Length);
            for (int offset = 0; offset < bytes.Length; offset += 16) Reply(bytes.Skip(offset).Take(16).ToArray());
            Reply(type);
        }
    }

    [Fact]
    public void DiscoveryIsSequentialAndAllRequestsAreLongReadOnlySwidSeven()
    {
        var h = new Rig(); h.Link();
        Assert.Single(h.Sent);
        Assert.Equal(new byte[] { 0x10, 4 }, h.Last.Parameters[..2]);
        h.Tracker.Pump(); Assert.Single(h.Sent);
        h.Reply(9); Assert.Equal(new byte[] { 0, 5 }, h.Last.Parameters[..2]);
        h.Reply(3); Assert.Equal((3, 0), ((int)h.Last.Feature, (int)h.Last.Function));
        h.Reply(11); Assert.Equal((3, 1, 0), ((int)h.Last.Feature, (int)h.Last.Function, (int)h.Last.Parameters[0]));
        h.Reply(Encoding.UTF8.GetBytes("MX Master 4"));
        Assert.Equal((3, 2), ((int)h.Last.Feature, (int)h.Last.Function));
        h.Reply(3); Assert.Equal((9, 1), ((int)h.Last.Feature, (int)h.Last.Function));
        h.Reply(0x55, 8, 0, 0);
        Assert.Equal(6, h.Sent.Count);
        Assert.Equal("MX Master 4", h.Device().Name);
        Assert.Equal(BatteryDeviceKind.Mouse, h.Device().Kind);
        Assert.All(h.Sent, frame =>
        {
            Assert.Equal(20, frame.Length); Assert.Equal(0x11, frame[0]); Assert.Equal(7, frame[3] & 15);
            h.Tracker.ValidateRequest(frame);
        });
    }
    [Fact]
    public void UnifiedStatusDecodesObservedMousePayload()
    {
        var h = new Rig(); h.Discover(); h.Reply(0x55, 8, 0, 0);
        Assert.Equal(new BatteryReading(85, BatteryLevel.Full, BatteryCharge.Discharging, false, false), h.Device().Reading);
        Assert.Equal(h.Utc, h.Device().UpdatedAt);
    }
    [Fact]
    public void NotificationUpdatesChargeWithoutCompletingPendingStatus()
    {
        var h = new Rig(); h.Discover(); int sent = h.Sent.Count;
        h.Receive(Harness.Frame(2, 9, 0, 0, 0x14, 2, 1, 0));
        Assert.Equal(20, h.Device().Reading!.Percent);
        Assert.Equal(BatteryCharge.Charging, h.Device().Reading!.Charge);
        h.Advance(1); Assert.Equal(sent + 1, h.Sent.Count);
        Assert.Equal(1, h.Last.Function);
    }
    [Theory]
    [InlineData(2, 9, 1, 11)]
    [InlineData(3, 9, 1, 7)]
    [InlineData(2, 8, 1, 7)]
    [InlineData(2, 9, 0, 7)]
    [InlineData(2, 9, 1, 0)]
    public void UnrelatedReplyDoesNotUpdateOrAdvance(int device, int feature, int fn, int swid)
    {
        var h = new Rig(); h.Discover(); int count = h.Sent.Count;
        h.Receive(Harness.Frame((byte)device, (byte)feature, (byte)fn, (byte)swid, 90, 8, 1, 0));
        Assert.Null(h.Device().Reading); Assert.Equal(count, h.Sent.Count);
        h.Advance(1); Assert.Equal(count + 1, h.Sent.Count);
    }
    [Fact]
    public void LegacyFallbackReadsFortyPercent()
    {
        var h = new Rig(); h.Discover("MX Keys S", legacy: true, type: 0);
        Assert.Equal(new byte[] { 0x10, 0 }, HidReport.Parse(h.Sent[1])!.Parameters[..2]);
        Assert.Equal((8, 0), ((int)h.Last.Feature, (int)h.Last.Function));
        h.Reply(40, 0, 0);
        Assert.Equal(40, h.Device().Reading!.Percent); Assert.Equal(BatteryDeviceKind.Keyboard, h.Device().Kind);
        h.Receive(Harness.Frame(2, 8, 0, 0, 39, 0, 2));
        Assert.Equal(39, h.Device().Reading!.Percent); Assert.Equal(BatteryCharge.Charging, h.Device().Reading!.Charge);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorsSkipQuestionAndContinue(bool shortReport)
    {
        var h = new Rig(); h.Link(); h.Error(shortReport);
        Assert.Equal(new byte[] { 0x10, 0 }, h.Last.Parameters[..2]);
        h.Reply(8); h.Reply(3); h.Error(shortReport); // 名前の長さの失敗 → 種類へ
        Assert.Equal((3, 2), ((int)h.Last.Feature, (int)h.Last.Function));
        h.Error(shortReport); Assert.Equal((8, 0), ((int)h.Last.Feature, (int)h.Last.Function));
        h.Reply(40, 0, 0); Assert.Equal(40, h.Device().Reading!.Percent);
    }
    [Fact]
    public void TimeoutRetriesTwiceThenWaitsForLink()
    {
        var h = new Rig(); h.Link();
        h.Advance(.999); Assert.Single(h.Sent);
        h.Advance(.001); h.Advance(1); Assert.Equal(3, h.Sent.Count);
        Assert.All(h.Sent, frame => Assert.Equal(h.Sent[0], frame));
        h.Advance(1); h.Advance(3600); Assert.Equal(3, h.Sent.Count);
        h.Link(); Assert.Equal(4, h.Sent.Count);
        h.Reply(9); Assert.Equal(new byte[] { 0, 5 }, h.Last.Parameters[..2]);
    }
    [Fact]
    public void DisconnectKeepsValueAndKnownReconnectOnlyRequestsBattery()
    {
        var h = new Rig(); h.Discover(); h.Reply(85, 8, 0, 0); var before = h.Device();
        h.Link(up: false); Assert.Equal(before with { Connected = false }, h.Device());
        int count = h.Sent.Count; h.Advance(3600); Assert.Equal(count, h.Sent.Count);
        h.Link(); Assert.True(h.Device().Connected);
        Assert.Equal((9, 1), ((int)h.Last.Feature, (int)h.Last.Function));
    }
    [Fact]
    public void RefreshUsesConfiguredIntervalAndDoesNotReviveTimedOutDevice()
    {
        var h = new Rig(new() { RefreshMinutes = 2 }); h.Discover(); h.Reply(85, 8, 0, 0);
        int count = h.Sent.Count; h.Advance(119); Assert.Equal(count, h.Sent.Count);
        h.Advance(1); Assert.Equal(count + 1, h.Sent.Count);
        Assert.Equal((9, 1), ((int)h.Last.Feature, (int)h.Last.Function));
        h.Advance(1); h.Advance(1); h.Advance(1); h.Advance(600);
        Assert.Equal(count + 3, h.Sent.Count);
    }
    [Fact]
    public void WriteFunctionsUnknownFeaturesAndBadFramesAreRejected()
    {
        var h = new Rig(); h.Discover();
        foreach (byte[] raw in new[]
        {
            HidReport.Request(2, 13, 3, [0, 0x52, 3, 0, 0x52], swid: 7),
            HidReport.Request(2, 9, 2, [], swid: 7),
            HidReport.Request(2, 3, 3, [], swid: 7),
            HidReport.Request(2, 0, 0, [0x1B, 4], swid: 7),
            HidReport.Request(2, 9, 1, []),
            HidReport.Request(2, 9, 1, [], shortReport: true, swid: 7),
            HidReport.Request(2, 9, 1, [1], swid: 7),
            HidReport.Request(3, 9, 1, [], swid: 7), new byte[0]
        }) Assert.Throws<InvalidOperationException>(() => h.Tracker.ValidateRequest(raw));
    }
    [Fact]
    public void BatteryRepliesLeaveGapSessionActiveWithoutWritesOrNotices()
    {
        var h = new Harness(); h.Activate(); Assert.Equal(SessionState.Active, h.Session.State);
        int count = h.Sent.Count; var notices = new List<string>(); h.Session.SwitchNotice += notices.Add;
        foreach (byte[] raw in new[]
        {
            Harness.Frame(2, 0, 0, 7, 9), Harness.Frame(2, 9, 1, 7, 85, 8, 0, 0),
            Harness.Frame(2, 3, 0, 7, 11), Harness.Frame(2, 3, 1, 7, Encoding.UTF8.GetBytes("MX Master 4")),
            Harness.Frame(2, 3, 2, 7, 3)
        }) { h.Session.Receive(raw); h.Session.Pump(); Assert.Equal(SessionState.Active, h.Session.State); }
        Assert.Equal(count, h.Sent.Count); Assert.Empty(notices);
    }
    [Fact]
    public void OpeningProbesAllSixSlotsSeriallyAndOnlySuccessfulPingLinks()
    {
        var h = new Rig(); h.Tracker.ReceiverOpened();
        for (byte slot = 1; slot <= 6; slot++)
        {
            Assert.Equal(slot, h.Last.Device); Assert.Equal((0, 1), ((int)h.Last.Feature, (int)h.Last.Function));
            Assert.Equal(7, h.Last.Swid);
            if (slot == 2) h.Reply(4, 5, 0x7A); else h.Error();
        }
        Assert.Equal(2, h.Last.Device); Assert.Equal(new byte[] { 0x10, 4 }, h.Last.Parameters[..2]);
        Assert.True(h.Device().Connected);
        Assert.All(h.Tracker.Snapshot.Where(d => d.Id != h.Device().Id), d => Assert.False(d.Connected));
    }
    [Fact]
    public void ReceiverLostCancelsPendingAndRetainsLastValue()
    {
        var h = new Rig(); h.Discover(); h.Reply(85, 8, 0, 0);
        h.Advance(600); int count = h.Sent.Count;
        h.Tracker.ReceiverLost(); h.Advance(100); Assert.Equal(count, h.Sent.Count);
        Assert.False(h.Device().Connected); Assert.Equal(85, h.Device().Reading!.Percent);
        h.Tracker.ReceiverOpened(); Assert.Equal(1, h.Last.Device); Assert.Equal(1, h.Last.Function);
        Assert.False(h.Device().Connected);
    }
    [Fact]
    public void DifferentDevicesNeverHaveSimultaneousOutstandingRequests()
    {
        var h = new Rig(); h.Link(2); h.Link(3); Assert.Single(h.Sent);
        h.Reply(9); Assert.Equal(3, h.Last.Device); Assert.Equal(2, h.Sent.Count);
        h.Reply(8); Assert.Equal(2, h.Last.Device); Assert.Equal(3, h.Sent.Count);
        Assert.Equal(new byte[] { 0, 5 }, h.Last.Parameters[..2]);
    }
    [Fact]
    public void NamesAreReassembledAsUtf8AcrossReportBoundaries()
    {
        var h = new Rig(); const string name = "123456789012345機器の名前";
        h.Discover(name); h.Reply(75, 4, 0, 0);
        Assert.Equal(name, h.Device().Name);
        var chunks = h.Sent.Select(raw => HidReport.Parse(raw)!).Where(r => r.Feature == 3 && r.Function == 1).ToArray();
        Assert.Equal(new byte[] { 0, 16 }, chunks.Select(r => r.Parameters[0]).ToArray());
    }
    [Fact]
    public void DisablingCancelsWorkAndEnablingProbesAgain()
    {
        var h = new Rig(); h.Link();
        h.Tracker.UpdateSettings(new() { Logitech = false }); h.Advance(20); h.Link();
        Assert.Single(h.Sent);
        h.Tracker.UpdateSettings(new()); Assert.Equal(2, h.Sent.Count);
        Assert.Equal((1, 0, 1), ((int)h.Last.Device, (int)h.Last.Feature, (int)h.Last.Function));
    }
    [Theory]
    [InlineData(1, 5)] [InlineData(2, 20)] [InlineData(4, 50)] [InlineData(8, 90)] [InlineData(10, 20)]
    public void LevelOnlyBatteryIsApproximate(int level, int percent)
    {
        var result = BatteryMath.Decode([0, (byte)level, 0, 0], true)!;
        Assert.Equal(percent, result.Percent); Assert.True(result.Approximate);
    }
    [Theory]
    [InlineData(0, BatteryCharge.Discharging)] [InlineData(1, BatteryCharge.Charging)]
    [InlineData(2, BatteryCharge.SlowCharging)] [InlineData(3, BatteryCharge.Full)]
    [InlineData(4, BatteryCharge.Error)] [InlineData(255, BatteryCharge.Unknown)]
    public void UnifiedChargeAndExternalPower(int state, BatteryCharge expected)
    {
        var result = BatteryMath.Decode([50, 4, (byte)state, 1], true)!;
        Assert.Equal(expected, result.Charge); Assert.True(result.ExternalPower);
    }
    [Fact]
    public void InvalidBatteryPayloadIsIgnoredAndTrueZeroIsNotEstimated()
    {
        Assert.Null(BatteryMath.Decode([101, 8, 0, 0], true));
        Assert.Null(BatteryMath.Decode([50, 4, 0], true));
        var zero = BatteryMath.Decode([0, 0, 0, 0], true)!;
        Assert.Equal(0, zero.Percent); Assert.False(zero.Approximate);
        var h = new Rig(); h.Discover(); h.Reply(85, 8, 0, 0);
        h.Receive(Harness.Frame(2, 9, 0, 0, 255, 8, 0, 0)); Assert.Equal(85, h.Device().Reading!.Percent);
    }
    [Theory]
    [InlineData("WH-1000XM6 Hands-Free AG", "WH-1000XM6")]
    [InlineData("WH-1000XM6 Hands-Free", "WH-1000XM6")]
    [InlineData("WH-1000XM6 Avrcp Transport", "WH-1000XM6")]
    [InlineData("WH-1000XM6", "WH-1000XM6")]
    [InlineData("Hands-Free AG device", "Hands-Free AG device")]
    public void BluetoothNamesRemoveOnlyKnownSuffix(string name, string expected) => Assert.Equal(expected, BatteryMath.BluetoothName(name));
    [Fact]
    public void TooltipIncludesChargeApproximationAndLastSeenWhenDisconnected()
    {
        var h = new Rig(); h.Discover(); h.Reply(0, 8, 1, 0);
        Assert.Equal("MX Master 4 90%（充電中）（概算）", BatteryMath.Tooltip(h.Device()));
        h.Link(up: false);
        string expectedTime = h.Utc.ToLocalTime().ToString("HH:mm");
        Assert.Equal($"MX Master 4 90%（切断中・{expectedTime} 時点）（概算）", BatteryMath.Tooltip(h.Device()));
    }
    [Theory]
    [InlineData(100, 0x34, 0xC7, 0x59)]
    [InlineData(21, 0x34, 0xC7, 0x59)]
    [InlineData(20, 255, 149, 0)]
    [InlineData(11, 255, 149, 0)]
    [InlineData(10, 255, 59, 48)]
    [InlineData(0, 255, 59, 48)]
    public void RingColorDependsOnlyOnRemainingAmount(int percent, int r, int g, int b)
    {
        // 11/20：輪の色は残量だけ（緑・オレンジ・赤）。充電中かどうかは稲妻で見せる。
        Assert.Equal(((byte)r, (byte)g, (byte)b), BatteryMath.RingColor(percent, lowWarn: 20, lowHot: 10));
    }
    [Theory]
    [InlineData(BatteryCharge.Charging, true)]
    [InlineData(BatteryCharge.SlowCharging, true)]
    [InlineData(BatteryCharge.Full, true)]
    [InlineData(BatteryCharge.Discharging, false)]
    [InlineData(BatteryCharge.Unknown, false)]
    [InlineData(BatteryCharge.Error, false)]
    public void BoltShowsWhilePluggedIn(BatteryCharge charge, bool expected)
    {
        Assert.Equal(expected, BatteryMath.ShowsBolt(new BatteryReading(50, BatteryLevel.Good, charge, false, false)));
        Assert.False(BatteryMath.ShowsBolt(null));
    }
    [Fact]
    public void MouseTooltipCarriesLogiGapStatusOnSecondLineWithinLimit()
    {
        // 11/20：LogiGap の状態は、温度のアイコンからマウスの電池のアイコンの説明へ移した。
        var h = new Rig(); h.Discover(); h.Reply(0, 8, 1, 0);
        Assert.Equal("MX Master 4 90%（充電中）（概算）\nLogiGap: 肩代わり中", BatteryMath.Tooltip(h.Device(), "LogiGap: 肩代わり中"));
        Assert.Equal(BatteryMath.Tooltip(h.Device()), BatteryMath.Tooltip(h.Device(), null));
        Assert.Equal(127, BatteryMath.Tooltip(h.Device(), new string('あ', 200)).Length);
    }
    [Fact]
    public void SettingsDefaultsAndJsonRoundTrip()
    {
        var defaults = JsonSerializer.Deserialize<AppSettings>("{}", AppSettings.JsonOptions)!; defaults.Validate();
        var b = defaults.Battery;
        Assert.True(b.Enabled && b.Logitech && b.Bluetooth); Assert.Empty(b.Hidden);
        Assert.Equal((20, 10, 10, 60), (b.LowWarn, b.LowHot, b.RefreshMinutes, b.BluetoothPollSeconds));
        var settings = defaults with { Battery = new() { Enabled = false, Logitech = false, Bluetooth = false,
            LowHot = 5, LowWarn = 25, Hidden = ["WH-1000XM6"], RefreshMinutes = 3, BluetoothPollSeconds = 20 } };
        string json = JsonSerializer.Serialize(settings, AppSettings.JsonOptions);
        var copy = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!; copy.Validate();
        Assert.Equal(json, JsonSerializer.Serialize(copy, AppSettings.JsonOptions));
        Assert.Equal("WH-1000XM6", Assert.Single(copy.Battery.Hidden));
        Assert.Contains("\"battery\"", json);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{\"hidden\":null}")]
    [InlineData("{\"lowHot\":0}")] [InlineData("{\"lowHot\":101}")]
    [InlineData("{\"lowWarn\":0}")] [InlineData("{\"lowWarn\":101}")]
    [InlineData("{\"lowHot\":20}")] [InlineData("{\"lowHot\":21}")]
    [InlineData("{\"refreshMinutes\":0}")] [InlineData("{\"refreshMinutes\":121}")]
    [InlineData("{\"bluetoothPollSeconds\":9}")] [InlineData("{\"bluetoothPollSeconds\":601}")]
    public void InvalidSettingsThrow(string battery)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>($"{{\"battery\":{battery}}}", AppSettings.JsonOptions)!;
        Assert.Throws<InvalidDataException>(() => settings.Validate());
    }
    [Theory]
    [InlineData(1, 2, 1, 10)] [InlineData(99, 100, 120, 600)]
    public void SettingsAcceptBoundaries(int hot, int warn, int refresh, int poll) =>
        new AppSettings { Battery = new() { LowHot = hot, LowWarn = warn, RefreshMinutes = refresh, BluetoothPollSeconds = poll } }.Validate();
    [Theory]
    [InlineData(16)] [InlineData(20)] [InlineData(24)] [InlineData(32)]
    public void DeviceGlyphsFitTheSameRowsAsMonitorNumbers(int size)
    {
        int height = MonitorFont.TempMemoryHeight(size);
        foreach (var kind in Enum.GetValues<BatteryDeviceKind>())
        {
            var glyph = MonitorFont.DeviceGlyph(kind, height);
            Assert.Equal(height, glyph.GetLength(0)); Assert.True(glyph.GetLength(1) <= size);
            Assert.Contains(glyph.Cast<bool>(), p => p);
        }
    }
    [Fact]
    public void ExistingRequestsStillDefaultToSwidFive()
    {
        Assert.Equal(5, HidReport.Parse(HidReport.Request(2, 0, 0, [0, 5]))!.Swid);
        Assert.Throws<ArgumentOutOfRangeException>(() => HidReport.Request(2, 0, 0, [], swid: 16));
    }
}
