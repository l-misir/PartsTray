// 判定スレッドで得た座標・窓クラス・各方式の結果を、題名を取得せず診断の一行に整形します。
using System.Text;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed class ScrollProbeDetails
{
    public Rectangle? Monitor;
    public PixelPoint? Inside;
    public string SourceClass = "-", InsideClass = "-", Geometry = "unknown";
    public bool? Maximized, CoversMonitor;
    public bool UsedInside;
    public string Native = "-", NativeInside = "-", Uia = "Skipped", WaitingAt = "-";
    public AutomationPointResult? DirectUia, InsideUia;
    public long WaitMs;

    public static string WindowClass(nint window)
    {
        var name = new StringBuilder(256);
        return window != 0 && N.GetClassNameW(window, name, name.Capacity) > 0
            ? name.ToString().Replace('\r', ' ').Replace('\n', ' ') : "-";
    }

    public void Write(ScrollProbeLog log, PixelPoint cursor, bool assist, string result, long ageAtCheckMs, PixelPoint probePoint)
    {
        string distances = Monitor is { } m
            ? $"{probePoint.X - m.Left},{m.Right - 1 - probePoint.X},{probePoint.Y - m.Top},{m.Bottom - 1 - probePoint.Y}" : "-";
        string clamped = cursor == probePoint ? "" : $" clamped={Point(probePoint)}";
        string fields = $"cursor={Point(cursor)}{clamped} edgeLRTB={distances} assist={Flag(assist)} usedInside={Flag(UsedInside)} " +
            $"edge={Geometry} inside={Point(Inside)} class={SourceClass} insideClass={InsideClass} " +
            $"maximized={Flag(Maximized)} coversMonitor={Flag(CoversMonitor)} result={result} " +
            $"native={Native}/{NativeInside} uia={Uia} waitingAt={WaitingAt} directUIA=[{DirectUia?.Reason ?? "-"}] " +
            $"insideUIA=[{InsideUia?.Reason ?? "-"}] cache={Flag(DirectUia?.Cached)}/{Flag(InsideUia?.Cached)}";
        string timing = $"uiaWaitMs={WaitMs} queryMs={DirectUia?.QueryMilliseconds.ToString() ?? "-"}/{InsideUia?.QueryMilliseconds.ToString() ?? "-"} ageAtCheckMs={ageAtCheckMs}";
        log.Record(fields, timing, Environment.TickCount64);
    }

    private static string Point(PixelPoint? point) => point is { } p ? $"({p.X},{p.Y})" : "-";
    private static string Flag(bool? value) => value switch { true => "yes", false => "no", null => "-" };
}
