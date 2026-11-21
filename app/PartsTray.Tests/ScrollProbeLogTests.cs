// 実機に触れず、判定とホイール送信の結果、静止・終了時の繰り返し件数を検査します。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class ScrollProbeLogTests
{
    [Fact]
    public void WheelDeliveryRecordsActualCountAndExplicitTargetForEachTransport()
    {
        var log = new FakeLog();
        var probe = new ScrollProbeLog(log);
        var input = new PointerEvent(MouseEventKind.Wheel, new(2561, -1223), Delta: -2000);
        var target = new EdgeWheelTarget(37, new(2553, -1223));

        probe.RecordWheel(input, default, true, 0);
        probe.RecordWheel(input, default, true, 10);
        probe.RecordWheel(input, default, false, 20);
        probe.RecordWheel(input, target, true, 30);

        Assert.Equal(new[]
        {
            "ScrollProbe wheel cursor=(2561,-1223) target=- axis=vertical delta=-2000 transport=SendInput requested=1 sent=1",
            "ScrollProbe wheel cursor=(2561,-1223) target=- axis=vertical delta=-2000 transport=SendInput requested=1 sent=1 繰り返し=1回",
            "ScrollProbe wheel cursor=(2561,-1223) target=- axis=vertical delta=-2000 transport=SendInput requested=1 sent=0",
            "ScrollProbe wheel cursor=(2561,-1223) target=(2553,-1223) axis=vertical delta=-2000 transport=PostMessage requested=1 sent=1"
        }, log.Lines);
    }

    [Fact]
    public void SteadyProbesCombineOncePerSecondAndUseLatestTiming()
    {
        var log = new FakeLog();
        var probe = new ScrollProbeLog(log);
        probe.Record("位置=(1,2) 結果=内側", "UIA=31ms", 0);
        for (int i = 1; i < 50; i++)
            probe.Record("位置=(1,2) 結果=内側", $"UIA={i}ms", i * 20);
        Assert.Single(log.Lines);
        probe.Record("位置=(1,2) 結果=内側", "UIA=0ms", 1000);
        probe.Record("位置=(1,2) 結果=内側", "UIA=5ms", 1020);
        probe.FlushDue(1999);
        Assert.Equal(2, log.Lines.Count);
        probe.FlushDue(2000);
        probe.FlushDue(3000);
        Assert.Equal(new[]
        {
            "ScrollProbe 位置=(1,2) 結果=内側 UIA=31ms",
            "ScrollProbe 位置=(1,2) 結果=内側 UIA=0ms 繰り返し=50回",
            "ScrollProbe 位置=(1,2) 結果=内側 UIA=5ms 繰り返し=1回"
        }, log.Lines);
    }

    [Fact]
    public void ChangedResultOrPositionAndResetFlushThePreviousRun()
    {
        var log = new FakeLog();
        var probe = new ScrollProbeLog(log);
        probe.Record("位置=A 結果=枠", "UIA=5ms", 10);
        probe.Record("位置=A 結果=枠", "UIA=7ms", 20);
        probe.Record("位置=A 結果=内側", "UIA=3ms", 30);
        probe.Record("位置=B 結果=内側", "UIA=6ms", 40);
        probe.Record("位置=B 結果=内側", "UIA=2ms", 50);
        probe.Reset();
        probe.Reset();
        probe.Record("位置=B 結果=内側", "UIA=4ms", 60);
        Assert.Equal(new[]
        {
            "ScrollProbe 位置=A 結果=枠 UIA=5ms",
            "ScrollProbe 位置=A 結果=枠 UIA=7ms 繰り返し=1回",
            "ScrollProbe 位置=A 結果=内側 UIA=3ms",
            "ScrollProbe 位置=B 結果=内側 UIA=6ms",
            "ScrollProbe 位置=B 結果=内側 UIA=2ms 繰り返し=1回",
            "ScrollProbe 位置=B 結果=内側 UIA=4ms"
        }, log.Lines);
    }
}
