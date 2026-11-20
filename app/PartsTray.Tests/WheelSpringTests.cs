using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

// 指示書I：バネの動き（臨界減衰）。Codex が本体を書いた後、使用量の上限で止まったため、テストは Claude が書いた。
public sealed class WheelSpringTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(8);

    private static WheelSpringSmoother Create(int springMs = 50)
    {
        var s = new WheelSpringSmoother(); s.Configure(springMs, 8); return s;
    }

    // 終わるまで 8ms ごとに Take し、各回の量を返す。
    private static List<int> Run(WheelSpringSmoother s, TimeSpan start, int maxTicks = 200)
    {
        var outputs = new List<int>();
        var now = start;
        for (int i = 0; i < maxTicks && s.NextDue is not null; i++, now += Tick)
            outputs.Add(s.Take(now));
        return outputs;
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(-1200)]
    [InlineData(1)]
    [InlineData(-7)]
    public void SendsExactlyTheAddedAmountAndStops(int amount)
    {
        var s = Create(); s.Add(amount, TimeSpan.Zero);
        var outputs = Run(s, TimeSpan.Zero);
        Assert.Equal(amount, outputs.Sum());
        Assert.Null(s.NextDue);
        Assert.True(outputs.Count * 8 <= 500, $"終わるまで {outputs.Count * 8}ms");
        Assert.All(outputs, o => Assert.True(o == 0 || Math.Sign(o) == Math.Sign(amount)));
    }

    [Fact]
    public void StartsAndEndsSlowly()
    {
        var s = Create(); s.Add(1200, TimeSpan.Zero);
        var moving = Run(s, TimeSpan.Zero).Where(o => o != 0).ToList();
        int peak = moving.Max();
        Assert.True(moving[0] < peak, $"最初 {moving[0]} / 最大 {peak}");
        Assert.True(moving[^1] < peak, $"最後 {moving[^1]} / 最大 {peak}");
    }

    [Fact]
    public void LargerAmountMovesFasterWithSimilarDuration()
    {
        var small = Create(); small.Add(1200, TimeSpan.Zero);
        var large = Create(); large.Add(2400, TimeSpan.Zero);
        var a = Run(small, TimeSpan.Zero); var b = Run(large, TimeSpan.Zero);
        Assert.True(b.Max() > a.Max());
        Assert.InRange(b.Count - a.Count, -3, 3);
    }

    [Fact]
    public void SameDirectionAdditionsAccumulate()
    {
        var s = Create(); s.Add(600, TimeSpan.Zero);
        int sent = s.Take(TimeSpan.Zero) + s.Take(Tick) + s.Take(2 * Tick);
        s.Add(600, 2 * Tick + TimeSpan.FromMilliseconds(3));
        sent += Run(s, 3 * Tick).Sum();
        Assert.Equal(1200, sent);
    }

    [Fact]
    public void ReversalDropsTheRemainderAndMovesOnlyTheNewWay()
    {
        var s = Create(); s.Add(1200, TimeSpan.Zero);
        int before = 0; var now = TimeSpan.Zero;
        for (int i = 0; i < 4; i++, now += Tick) before += s.Take(now);
        Assert.InRange(before, 1, 1199);
        s.Add(-600, now);
        var after = Run(s, now);
        Assert.All(after, o => Assert.True(o <= 0));
        Assert.Equal(-600, after.Sum());
    }

    [Fact]
    public void LateTakeReturnsOneStepWithoutBursting()
    {
        var s = Create(); s.Add(1200, TimeSpan.Zero);
        int first = s.Take(TimeSpan.Zero);
        var late = TimeSpan.FromMilliseconds(300);
        int second = s.Take(late);
        Assert.InRange(first + second, 0, 1200);
        Assert.Equal(late + Tick, s.NextDue);
        Assert.Equal(1200, first + second + Run(s, late + Tick).Sum());
    }

    [Fact]
    public void ResetAndConfigureDropTheRemainder()
    {
        var s = Create(); s.Add(1200, TimeSpan.Zero); s.Take(TimeSpan.Zero);
        s.Reset();
        Assert.Null(s.NextDue); Assert.Equal(0, s.Take(Tick));
        s.Add(1200, TimeSpan.Zero); s.Configure(80, 8);
        Assert.Null(s.NextDue); Assert.Equal(0, s.Take(Tick));
    }

    [Fact]
    public void SettingsDefaultsAndJson()
    {
        var thumb = new ThumbSettings();
        Assert.Equal("linear", thumb.SmoothMode);
        Assert.Equal(50, thumb.SpringMs);
        var config = JsonSerializer.Deserialize<AppSettings>(
            "{\"gapFiller\":{\"thumbWheel\":{\"smoothMode\":\"spring\",\"springMs\":120,\"speedPercent\":300}}}", AppSettings.JsonOptions)!;
        config.Validate();
        Assert.Equal("spring", config.GapFiller.ThumbWheel.SmoothMode);
        Assert.Equal(120, config.GapFiller.ThumbWheel.SpringMs);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(config, AppSettings.JsonOptions));
        var json = saved.RootElement.GetProperty("gapFiller").GetProperty("thumbWheel");
        Assert.Equal("spring", json.GetProperty("smoothMode").GetString());
        Assert.Equal(120, json.GetProperty("springMs").GetInt32());
    }

    [Theory]
    [InlineData("Spring", 50, 100, "thumbWheel.smoothMode は linear / spring のどちらかです")]
    [InlineData("", 50, 100, "thumbWheel.smoothMode は linear / spring のどちらかです")]
    [InlineData("spring", 9, 100, "thumbWheel.springMs は 10〜500 です")]
    [InlineData("spring", 501, 100, "thumbWheel.springMs は 10〜500 です")]
    [InlineData("spring", 50, 301, "ホイール設定または後片付け時間が不正です")]
    public void InvalidSettingsAreRejected(string mode, int springMs, int speed, string message)
    {
        var config = new AppSettings { GapFiller = new() { Enabled = true, ThumbWheel = new() { SmoothMode = mode, SpringMs = springMs, SpeedPercent = speed } } };
        Assert.Equal(message, Assert.Throws<InvalidDataException>(config.Validate).Message);
    }

    private static Harness SpringSession() =>
        new(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "hook", SpeedPercent = 100, DirectionSign = 1, SmoothMs = 40, SmoothMode = "spring", SpringMs = 50 } } });

    private static void PumpFor(Harness h, int ticks, bool checkWait = false)
    {
        for (int i = 0; i < ticks; i++)
        {
            h.Session.Pump();
            if (checkWait && h.Input.WheelEvents.Sum() != -120) Assert.True(h.Session.WorkerWaitMilliseconds <= 8);
            h.Clock.Now += Tick;
        }
    }

    [Fact]
    public void SessionSendsTheWholeAmountSmoothly()
    {
        var h = SpringSession(); h.Activate();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.True(h.Session.TryTakeHorizontal(120));
        PumpFor(h, 100, checkWait: true);
        Assert.Equal(-120, h.Input.WheelEvents.Sum());
        Assert.DoesNotContain(0, h.Input.WheelEvents);
        Assert.True(h.Input.WheelEvents.Count > 1);
    }

    [Fact]
    public void SessionPauseDropsTheRemainder()
    {
        var h = SpringSession(); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(1200));
        PumpFor(h, 3);
        int sent = h.Input.WheelEvents.Sum();
        Assert.InRange(sent, -1199, -1);
        h.Session.SetPaused(true);
        PumpFor(h, 100);
        Assert.Equal(sent, h.Input.WheelEvents.Sum());
    }

    [Fact]
    public void SessionKeepsSendingTheRemainderAfterOptionsTakesTheThumb()
    {
        var h = SpringSession(); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(1200));
        PumpFor(h, 3);
        h.Options(19, 2);
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.False(h.Session.TryTakeHorizontal(120));
        PumpFor(h, 100);
        Assert.Equal(-1200, h.Input.WheelEvents.Sum());
    }
}
