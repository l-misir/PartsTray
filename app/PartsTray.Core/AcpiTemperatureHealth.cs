// ACPI 温度の固定値を、経過時間と CPU 使用率から判定します。
// 判定の時間・使用率差・復帰規則を変えるときに開きます。
namespace PartsTray.Core;

public sealed class AcpiTemperatureHealth(Func<TimeSpan> now)
{
    public const string UnavailableMessage = "この PC は Windows から CPU 温度を読めません。設定の『温度の読み方』で Core Temp を選んでください";
    private CpuTemperature? previous;
    private TimeSpan since;
    private double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
    public bool IsUnavailable { get; private set; }
    private bool noticeSent, stuck;

    public void Reset()
    {
        previous = null;
        IsUnavailable = false; stuck = false;
        minimum = double.PositiveInfinity; maximum = double.NegativeInfinity;
    }

    public CpuTemperature? Observe(string source, CpuTemperature? value, double? usage)
    {
        if (source != "acpi") { Reset(); return value; }
        var time = now();
        if (value is null) { if (!stuck) Reset(); IsUnavailable = true; return null; }
        if (previous != value || time < since)
        {
            Reset(); previous = value; since = time;
        }
        if (usage is { } cpu && double.IsFinite(cpu) && cpu is >= 0 and <= 100)
        {
            minimum = Math.Min(minimum, cpu); maximum = Math.Max(maximum, cpu);
        }
        if (time - since >= TimeSpan.FromMinutes(5) && maximum - minimum >= 30)
            stuck = true;
        IsUnavailable = stuck;
        return IsUnavailable ? null : value;
    }

    // 復帰後に再発しても、記録と通知はこのインスタンスの生存中に一度だけ。
    public bool TakeNotice()
    {
        if (!IsUnavailable || noticeSent) return false;
        noticeSent = true; return true;
    }
}
