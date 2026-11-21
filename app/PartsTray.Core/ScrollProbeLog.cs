// 判定とホイール送信の診断を、それぞれのワーカーで同じ結果ごとにまとめます。
namespace PartsTray.Core;

public sealed class ScrollProbeLog(ILog log)
{
    private string? previousFields;
    private string latestSuffix = "";
    private long lastWriteMs;
    private int repeats;

    public void Record(string fields, string timing, long nowMs)
        => RecordFields(fields, $" {timing}", nowMs);

    // delta は送信に使った回転量。元の入力1回につき Windows へ1回だけ送る。
    public void RecordWheel(PointerEvent input, EdgeWheelTarget target, bool sent, long nowMs)
    {
        bool posted = target.Window != 0;
        string point = posted ? $"({target.Position.X},{target.Position.Y})" : "-";
        string axis = input.Kind == MouseEventKind.HorizontalWheel ? "horizontal" : "vertical";
        string transport = posted ? "PostMessage" : "SendInput";
        string fields = $"wheel cursor=({input.Position.X},{input.Position.Y}) target={point} axis={axis} " +
            $"delta={input.Delta} transport={transport} requested=1 sent={(sent ? 1 : 0)}";
        RecordFields(fields, "", nowMs);
    }

    private void RecordFields(string fields, string suffix, long nowMs)
    {
        if (fields != previousFields)
        {
            FlushRepeats();
            previousFields = fields;
            lastWriteMs = nowMs;
            log.Write($"ScrollProbe {fields}{suffix}");
            return;
        }

        latestSuffix = suffix;
        repeats++;
        FlushDue(nowMs);
    }

    public void FlushDue(long nowMs)
    {
        if (repeats == 0 || nowMs - lastWriteMs < 1000) return;
        FlushRepeats();
        lastWriteMs = nowMs;
    }

    public void Reset()
    {
        FlushRepeats();
        previousFields = null;
    }

    private void FlushRepeats()
    {
        if (repeats == 0) return;
        log.Write($"ScrollProbe {previousFields}{latestSuffix} 繰り返し={repeats}回");
        repeats = 0;
    }
}
