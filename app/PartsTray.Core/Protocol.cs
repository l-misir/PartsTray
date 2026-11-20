using System.Buffers.Binary;

namespace PartsTray.Core;

public enum ReportKind { Connection, Response, Notification, Error20, Error10, Other }

public sealed record HidReport(ReportKind Kind, byte Device, byte Feature, byte Function,
    byte Swid, byte[] Parameters, byte Error = 0)
{
    public ushort Wpid => Parameters.Length >= 3 ? (ushort)(Parameters[1] | Parameters[2] << 8) : (ushort)0;
    public bool LinkUp => Kind == ReportKind.Connection && (Parameters[0] & 0x40) == 0;

    public static HidReport? Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 7 || (raw[0] != 0x10 && raw[0] != 0x11)) return null;
        int length = raw[0] == 0x10 ? 7 : 20;
        if (raw.Length < length) return null;
        raw = raw[..length];
        byte dev = raw[1], feature = raw[2], code = raw[3];
        var p = raw[4..].ToArray();
        if (raw[0] == 0x10 && feature == 0x41)
            return new(ReportKind.Connection, dev, feature, 0, 0, p);
        if (raw[0] == 0x10 && feature == 0x8F)
            return new(ReportKind.Error10, dev, code, (byte)(p[0] >> 4), (byte)(p[0] & 15), p, p[1]);
        if (raw[0] == 0x11 && feature == 0xFF)
            return new(ReportKind.Error20, dev, code, (byte)(p[0] >> 4), (byte)(p[0] & 15), p, p[1]);
        if (dev == 0xFF || feature >= 0x80)
            return new(ReportKind.Other, dev, feature, 0, 0, p);
        return new((code & 15) == 0 ? ReportKind.Notification : ReportKind.Response,
            dev, feature, (byte)(code >> 4), (byte)(code & 15), p);
    }

    public static byte[] Request(byte device, byte feature, byte function, ReadOnlySpan<byte> parameters, bool shortReport = false, byte swid = 5)
    {
        if (device is < 1 or > 6 || feature >= 0x80 || function > 15 || swid > 15)
            throw new ArgumentOutOfRangeException(nameof(device));
        var result = new byte[shortReport ? 7 : 20];
        if (parameters.Length > result.Length - 4) throw new ArgumentException("Parameters too long");
        result[0] = shortReport ? (byte)0x10 : (byte)0x11;
        result[1] = device; result[2] = feature; result[3] = (byte)((function << 4) | swid);
        parameters.CopyTo(result.AsSpan(4));
        return result;
    }

    public static ushort U16(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt16BigEndian(p);
    public static byte[] Be(ushort value) => [(byte)(value >> 8), (byte)value];
}

public interface IClock { TimeSpan Now { get; } }
public interface IReportTransport
{
    // Submit immediately; never wait for a HID reply or enqueue a deferred write here.
    void Send(byte[] report);
}
public interface IInputSink { void Keys(string keys); void HorizontalWheel(int delta); }
public interface ILog { void Write(string message); }

public interface INativeWheelBridge
{
    // 低レベルフックのスレッドから呼ばれる。ロックを取らず、すぐ戻ること。
    // true を返したら、呼び出し側は元の入力を止める。
    bool TryTakeHorizontal(int delta);
}

public sealed class NativeWheelScaler
{
    private decimal remainder;
    public int Convert(int nativeDelta, decimal speedPercent, int directionSign)
    {
        // 要確認: 本来の横スクロールを反転し、Options+ の「反転」オンと同じ向きになるか実機で最終確認する。
        remainder += nativeDelta * speedPercent / 100m * -directionSign;
        int whole = (int)decimal.Truncate(remainder);
        remainder -= whole;
        return whole;
    }
    public void Reset() => remainder = 0;
}

public interface IWheelSmoother
{
    void Configure(int durationMs, int intervalMs);
    void Add(int amount, TimeSpan now);
    int Take(TimeSpan now);
    TimeSpan? NextDue { get; }
    void Reset();
}

public sealed class WheelSmoother : IWheelSmoother
{
    private int slices = 5, pending, slicesLeft;
    private TimeSpan interval = TimeSpan.FromMilliseconds(8), nextDue;
    public TimeSpan? NextDue => pending == 0 ? null : nextDue;

    public void Configure(int smoothMs, int intervalMs)
    {
        if (smoothMs is < 0 or > 200 || intervalMs is < 4 or > 50 ||
            (smoothMs != 0 && smoothMs < intervalMs))
            throw new ArgumentOutOfRangeException(nameof(smoothMs));
        // 要確認: smoothMs=0 を直接使う場合も即時に全量を返せるよう、分割数は最低 1 とする。
        slices = Math.Max(1, (smoothMs + intervalMs - 1) / intervalMs);
        interval = TimeSpan.FromMilliseconds(intervalMs);
        Reset();
    }
    public void Add(int amount, TimeSpan now)
    {
        if (amount == 0) return;
        if (pending == 0 || Math.Sign(pending) != Math.Sign(amount))
        {
            pending = amount;
            nextDue = now;
        }
        else pending = checked(pending + amount);
        slicesLeft = slices;
    }
    public int Take(TimeSpan now)
    {
        if (pending == 0 || now < nextDue) return 0;
        int amount = (int)(Math.Sign(pending) * ((Math.Abs((long)pending) + slicesLeft - 1) / slicesLeft));
        pending -= amount;
        slicesLeft--;
        if (pending == 0) slicesLeft = 0;
        else
        {
            nextDue += interval;
            if (nextDue <= now) nextDue = now + interval;
        }
        return amount;
    }
    public void Reset() { pending = slicesLeft = 0; nextDue = default; }
}

public sealed class ThumbWheelConverter
{
    private decimal remainder;
    public int Convert(short rotation, decimal speedPercent, int directionSign)
    {
        remainder += rotation * 20m * speedPercent / 100m * directionSign;
        int whole = (int)decimal.Truncate(remainder);
        remainder -= whole;
        return whole;
    }
    public void Reset() => remainder = 0;
}
