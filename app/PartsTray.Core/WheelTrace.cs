using System.Globalization;

namespace PartsTray.Core;

public enum WheelTraceSource { PartsTray, Injected, Device }
public enum WheelTraceTaken { None, Gesture, Bridge }
// Vertical = 縦ホイール（WM_MOUSEWHEEL）。Options+ が横スクロールを縦ホイールで出していないかを確かめるために記録する。
public readonly record struct WheelTraceEntry(long Timestamp, int Delta, uint Flags, nuint Extra, WheelTraceTaken Taken, bool Vertical = false);

public static class WheelTrace
{
    public static WheelTraceSource Source(uint flags, nuint extra) =>
        extra == KeyStroke.ExtraInfo ? WheelTraceSource.PartsTray :
        (flags & 0x3) != 0 ? WheelTraceSource.Injected : WheelTraceSource.Device;

    public static string Header(DateTime start) => string.Create(CultureInfo.InvariantCulture,
        $"# 横スクロール記録 開始 {start:yyyy-MM-dd HH:mm:ss.fff} 列: 時刻 前からの間隔ms 量 送り元 flags extra 横取り（縦ホイールは行末に wheel=V）");

    // 横スクロールの行は今までと同じ形。縦ホイールの行だけ、行末に wheel=V を付ける。
    public static string Format(WheelTraceEntry entry, DateTime wallClock, double sincePreviousMs) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{wallClock:HH:mm:ss.fff} +{sincePreviousMs:F1} {entry.Delta} {Source(entry.Flags, entry.Extra)} flags={entry.Flags:X2} extra={entry.Extra:X} taken={entry.Taken}{(entry.Vertical ? " wheel=V" : "")}");
}
