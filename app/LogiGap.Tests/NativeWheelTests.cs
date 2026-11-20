using System.Text.Json;
using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class NativeWheelTests
{
    private static AppSettings Config(string mode = "hook", decimal speed = 50, int direction = 1, int smoothMs = 0) =>
        new() { GapFiller = new() { ThumbWheel = new() { Mode = mode, SpeedPercent = speed, DirectionSign = direction, SmoothMs = smoothMs } } };

    private static Harness PendingApply()
    {
        var h = new Harness(Config());
        h.Intercept = r =>
        {
            if (r[2] == h.Features[0x2201] && r[3] == 0x35) h.AutoReply = false;
            return false;
        };
        h.Activate();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Single(h.Writes);
        return h;
    }

    [Fact]
    public void SmoothedBridgeSendsImmediatelyThenEveryEightMilliseconds()
    {
        var h = new Harness(Config(smoothMs: 40)); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        Assert.Equal(new[] { -12 }, h.Input.WheelEvents);
        Assert.InRange(h.Session.WorkerWaitMilliseconds, 1, 8);
        h.Advance(0.004); Assert.Single(h.Input.WheelEvents);
        Assert.Equal(4, h.Session.WorkerWaitMilliseconds);
        h.Advance(0.004);
        for (int i = 0; i < 3; i++) h.Advance(0.008);
        Assert.Equal(new[] { -12, -12, -12, -12, -12 }, h.Input.WheelEvents);
        Assert.Equal(Timeout.Infinite, h.Session.WorkerWaitMilliseconds);
    }

    [Fact]
    public void SmoothedTailSurvivesOptionsTakeoverButIsNotAddedToSummary()
    {
        var h = new Harness(Config(smoothMs: 40)); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        h.Options(19, 2);
        string summary = Assert.Single(h.Log.Lines, l => l.Contains("橋渡しを終了"));
        Assert.Contains("受け取り 1 件（合計 120）、送信 1 件（合計 -12）", summary);
        for (int i = 0; i < 4; i++) h.Advance(0.008);
        Assert.Equal(new[] { -12, -12, -12, -12, -12 }, h.Input.WheelEvents);
        Assert.False(h.Session.TryTakeHorizontal(120));
        Assert.Equal(summary, Assert.Single(h.Log.Lines, l => l.Contains("橋渡しを終了")));
    }

    [Fact]
    public void PauseDiscardsSmoothedTailAndDeadline()
    {
        var h = new Harness(Config(smoothMs: 40)); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        h.Session.SetPaused(true);
        Assert.Equal(Timeout.Infinite, h.Session.WorkerWaitMilliseconds);
        h.Advance(0.1); h.Session.SetPaused(false); h.Advance(0.1);
        Assert.Equal(new[] { -12 }, h.Input.WheelEvents);
    }

    [Fact]
    public void SettingsReloadDiscardsSmoothedTailAndUsesNewIntervals()
    {
        var h = new Harness(Config(smoothMs: 40)); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        var config = Config(smoothMs: 60);
        config = config with { GapFiller = config.GapFiller with { ThumbWheel = config.GapFiller.ThumbWheel with { SmoothIntervalMs = 20 } } };
        h.Session.UpdateSettings(config);
        Assert.Equal(Timeout.Infinite, h.Session.WorkerWaitMilliseconds);
        h.Advance(0.008); Assert.Equal(new[] { -12 }, h.Input.WheelEvents);
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        Assert.Equal(20, h.Session.WorkerWaitMilliseconds);
        h.Advance(0.020); h.Advance(0.020);
        Assert.Equal(new[] { -12, -20, -20, -20 }, h.Input.WheelEvents);
    }

    [Fact]
    public void SmoothedFailuresConsumeSlicesWithoutBlockingHidOrRetrying()
    {
        var h = new Harness(Config(smoothMs: 40));
        h.Intercept = r =>
        {
            if (r[2] == h.Features[0x2201] && r[3] == 0x35) h.AutoReply = false;
            return false;
        };
        h.Activate(); h.Input.ThrowOnWheel = true;
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        h.Advance(0.008); h.Advance(0.008);
        Assert.Single(h.Log.Lines, l => l.StartsWith("サムホイール送信失敗:"));
        h.Input.ThrowOnWheel = false;
        h.Intercept = null; h.AutoReply = true; h.Reply(h.Sent[^1]);
        h.Advance(0.008); h.Advance(0.008); h.Advance(0.008);
        Assert.Equal(new[] { -12, -12 }, h.Input.WheelEvents);
        Assert.Equal(6, h.Writes.Length);
        Assert.DoesNotContain(h.Log.Lines, l => l.StartsWith("ワーカーエラー:"));
        Assert.Equal(Timeout.Infinite, h.Session.WorkerWaitMilliseconds);
        h.Options(19, 2);
        Assert.Contains(h.Log.Lines, l => l.Contains("受け取り 1 件（合計 120）、送信 2 件（合計 -24）"));
    }

    [Fact]
    public void FullyFailedSmoothingStillFinishes()
    {
        var h = new Harness(Config(smoothMs: 40)); h.Activate(); h.Input.ThrowOnWheel = true;
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        for (int i = 0; i < 4; i++) h.Advance(0.008);
        Assert.Equal(Timeout.Infinite, h.Session.WorkerWaitMilliseconds);
        h.Input.ThrowOnWheel = false; h.Advance(0.1);
        Assert.Empty(h.Input.WheelEvents);
        Assert.Single(h.Log.Lines, l => l.StartsWith("サムホイール送信失敗:"));
        h.Options(19, 2);
        Assert.Contains(h.Log.Lines, l => l.Contains("受け取り 1 件（合計 120）、送信 0 件（合計 0）"));
    }

    [Theory]
    [InlineData("{}", "hook")]
    [InlineData("{\"gapFiller\":{\"thumbWheel\":{\"mode\":\"hook\"}}}", "hook")]
    [InlineData("{\"gapFiller\":{\"thumbWheel\":{\"mode\":\"divert\"}}}", "divert")]
    [InlineData("{\"gapFiller\":{\"thumbWheel\":{\"mode\":\"off\"}}}", "off")]
    [InlineData("{\"gapFiller\":{\"thumbWheel\":{\"divert\":false}}}", "hook")]
    [InlineData("{\"gapFiller\":{\"thumbWheel\":{\"divert\":true}}}", "hook")]
    public void JsonModesAndLegacyKey(string json, string mode)
    {
        var config = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        config.Validate();
        Assert.Equal(mode, config.GapFiller.ThumbWheel.Mode);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(config, AppSettings.JsonOptions));
        var thumb = saved.RootElement.GetProperty("gapFiller").GetProperty("thumbWheel");
        Assert.Equal(mode, thumb.GetProperty("mode").GetString());
        Assert.False(thumb.TryGetProperty("divert", out _));
    }

    [Theory]
    [InlineData("Hook")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidModeIsRejected(string? mode)
    {
        var error = Assert.Throws<InvalidDataException>(() => Config(mode!).Validate());
        Assert.Equal("thumbWheel.mode は hook / divert / off のどれかです", error.Message);
    }

    [Theory]
    [InlineData(1, -60)]
    [InlineData(-1, 60)]
    public void HookScalesNativeInputWithoutThumbWrites(int direction, int expected)
    {
        var h = new Harness(Config(direction: direction)); h.Activate();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.DoesNotContain(h.Writes, r => r[2] == h.Features[0x2150]);
        Assert.False(h.Session.TryTakeHorizontal(0));
        Assert.True(h.Session.TryTakeHorizontal(120));
        Assert.Empty(h.Input.WheelEvents);
        h.Session.Pump();
        Assert.Equal(new[] { expected }, h.Input.WheelEvents);
        Assert.Contains(h.Log.Lines, l => l == "サムホイール: フック方式で橋渡しを開始（向き反転・速度 50%）");
    }

    [Fact]
    public void FractionalNativeDeltasAccumulate()
    {
        var h = new Harness(Config()); h.Activate();
        for (int i = 0; i < 3; i++) { Assert.True(h.Session.TryTakeHorizontal(1)); h.Session.Pump(); }
        Assert.Equal(new[] { -1 }, h.Input.WheelEvents);
    }

    [Fact]
    public void BridgeStartsOnlyAfterActivation()
    {
        var h = new Harness(Config());
        Assert.False(h.Session.TryTakeHorizontal(120));
        h.Link(); Assert.False(h.Session.TryTakeHorizontal(120));
        h.Cids[0x52] = 1; h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.False(h.Session.TryTakeHorizontal(120));
        h.Advance(30); Assert.Empty(h.Input.WheelEvents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionsThumbStopsBridgeAtOrAfterRetreat(bool dpiFirst)
    {
        var h = new Harness(Config()); h.Activate();
        if (dpiFirst)
        {
            h.Options(); Assert.Equal(SessionState.Retreated, h.Session.State);
            Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        }
        h.Options(19, 2);
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.False(h.Session.TryTakeHorizontal(120));
        Assert.Contains(h.Log.Lines, l => l.Contains("橋渡しを終了（Options+ がサムホイールを設定）"));
        h.Options(19, 2);
        Assert.Single(h.Log.Lines, l => l.Contains("橋渡しを終了"));
    }

    [Fact]
    public void BridgeExpiresAtCleanupDeadlineAndWorkerWakesInTime()
    {
        var h = new Harness(Config()); h.Activate(); h.Advance(10); h.Options();
        h.Advance(14.999);
        Assert.True(h.Session.TryTakeHorizontal(120));
        Assert.InRange(h.Session.WorkerWaitMilliseconds, 1, 1);
        h.Advance(0.001);
        Assert.False(h.Session.TryTakeHorizontal(120));
        Assert.Equal(new[] { -60 }, h.Input.WheelEvents);
        Assert.Contains(h.Log.Lines, l => l.Contains("橋渡しを終了（引き継ぎから 15 秒）"));
        Assert.Equal(Timeout.Infinite, h.Session.WorkerWaitMilliseconds);
    }

    [Theory]
    [InlineData("link", "リンク切断")]
    [InlineData("device", "リンク切断")]
    [InlineData("receiver", "レシーバー切断")]
    [InlineData("begin", "新しい接続")]
    [InlineData("pause", "一時停止")]
    [InlineData("resume", "再開")]
    public void LifecycleStopsBridge(string transition, string reason)
    {
        var h = new Harness(Config()); h.Activate();
        switch (transition)
        {
            case "link": h.Session.Receive([0x10, 2, 0x41, 0x10, 0x42, 0x42, 0xB0]); break;
            case "device": h.Session.Receive([0x10, 2, 0x41, 0x10, 2, 0x43, 0xB0]); break;
            case "receiver": h.Session.ReceiverLost(); break;
            case "begin": h.Link(); break;
            case "pause": h.Session.SetPaused(true); break;
            case "resume": h.Session.SetPaused(false); break;
        }
        Assert.False(h.Session.TryTakeHorizontal(120));
        Assert.Contains(h.Log.Lines, l => l.Contains($"橋渡しを終了（{reason}）"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedInputSurvivesStopExceptWhilePaused(bool pause)
    {
        var h = new Harness(Config()); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(120));
        if (pause) h.Session.SetPaused(true); else h.Options(19, 2);
        h.Session.Pump();
        Assert.Equal(pause ? Array.Empty<int>() : new[] { -60 }, h.Input.WheelEvents);
        h.Session.SetPaused(false); h.Session.Pump();
        Assert.Equal(pause ? Array.Empty<int>() : new[] { -60 }, h.Input.WheelEvents);
    }

    [Fact]
    public void PendingHidReplyDoesNotDelayWheelSending()
    {
        var h = PendingApply();
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        Assert.Equal(new[] { -60 }, h.Input.WheelEvents);
        Assert.Single(h.Writes);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("適用完了"));
    }

    [Fact]
    public void WheelFailureIsLoggedOnceAndDoesNotBlockHidApplication()
    {
        var h = PendingApply(); h.Input.ThrowOnWheel = true;
        for (int i = 0; i < 3; i++) { Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump(); }
        Assert.Single(h.Log.Lines, l => l.StartsWith("サムホイール送信失敗:"));
        h.Intercept = null; h.AutoReply = true;
        h.Reply(h.Sent[^1]); h.Session.Pump();
        Assert.Equal(6, h.Writes.Length);
        Assert.Contains(h.Log.Lines, l => l.StartsWith("適用完了"));
        Assert.DoesNotContain(h.Log.Lines, l => l.StartsWith("ワーカーエラー:"));
        h.Input.ThrowOnWheel = false;
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        Assert.Equal(new[] { -60 }, h.Input.WheelEvents);
    }

    [Fact]
    public void FailureLogIsNotRepeatedForTailButResetsOnNextBridge()
    {
        var h = new Harness(Config()); h.Activate(); h.Input.ThrowOnWheel = true;
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Options(19, 2); h.Session.Pump();
        Assert.Single(h.Log.Lines, l => l.StartsWith("サムホイール送信失敗:"));
        h.Cids[0x52] = 0; h.Link(); h.Session.Pump();
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        Assert.Equal(2, h.Log.Lines.Count(l => l.StartsWith("サムホイール送信失敗:")));
    }

    [Theory]
    [InlineData("hook")]
    [InlineData("off")]
    public void NonDivertModesNeverOwnOrCleanUpThumb(string mode)
    {
        var h = new Harness(Config(mode)); h.Activate();
        Assert.Equal(mode == "hook", h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0, 1, 0, 0, 1, 2)); h.Session.Pump();
        Assert.Equal(mode == "hook" ? new[] { -60 } : Array.Empty<int>(), h.Input.WheelEvents);
        if (mode == "off") Assert.DoesNotContain(h.Sent, r => r[2] == h.Features[0x2150]);
        else Assert.DoesNotContain(h.Writes, r => r[2] == h.Features[0x2150]);
        h.Thumb = 1; int beforeCleanup = h.Sent.Count;
        h.Options(); h.Advance(15);
        Assert.DoesNotContain(h.Sent.Skip(beforeCleanup), r => r[2] == h.Features[0x2150]);
        Assert.Equal(1, h.Thumb);
        Assert.Contains(h.Log.Lines, l => l.Contains("後片付け開始"));
    }

    [Theory]
    [InlineData(1, "サムホイール読み戻し OK 01 01")]
    [InlineData(3, "サムホイール読み戻し OK 01 03")]
    [InlineData(0, "サムホイール読み戻し不一致 01 00: 要確認")]
    public void DivertReadbackChecksInversionBit(byte status, string expected)
    {
        var h = new Harness(Config("divert"));
        h.Intercept = r =>
        {
            if (r[2] != 19 || r[3] != 0x15) return false;
            h.Session.Receive(Harness.Frame(2, 19, 1, 5, 1, status)); return true;
        };
        h.Activate();
        Assert.Contains(expected, h.Log.Lines);
        Assert.False(h.Session.TryTakeHorizontal(120));
        Assert.Equal(new byte[] { 1, 1 }, h.Writes[^1][4..6]);
    }

    [Fact]
    public void SettingsResetFractionAndApplySpeedImmediatelyButModeOnNextLink()
    {
        var h = new Harness(Config()); h.Activate();
        Assert.True(h.Session.TryTakeHorizontal(1)); h.Session.Pump();
        h.Session.UpdateSettings(Config("off", 25, -1));
        Assert.True(h.Session.TryTakeHorizontal(2)); h.Session.Pump();
        Assert.Empty(h.Input.WheelEvents);
        Assert.True(h.Session.TryTakeHorizontal(2)); h.Session.Pump();
        Assert.Equal(new[] { 1 }, h.Input.WheelEvents);
        int beforeReconnect = h.Sent.Count;
        h.Link(); h.Session.Pump();
        Assert.False(h.Session.TryTakeHorizontal(120));
        Assert.DoesNotContain(h.Sent.Skip(beforeReconnect), r => r[2] == 19);
    }

    [Fact]
    public void SummaryCountsSuccessfulSendsAndKeepsFirstFiveExamples()
    {
        var h = new Harness(Config()); h.Activate();
        for (int i = 1; i <= 7; i++) Assert.True(h.Session.TryTakeHorizontal(120 * i));
        h.Session.Pump(); h.Options(19, 2);
        Assert.Contains("サムホイール: 橋渡しを終了（Options+ がサムホイールを設定） 受け取り 7 件（合計 3360）、送信 7 件（合計 -1680）、元の量の例 [120,240,360,480,600]", h.Log.Lines);
    }

    [Fact]
    public void WakeFailureDoesNotReturnAlreadyAcceptedInputToCaller()
    {
        var h = new Harness(Config()); h.Activate();
        h.Session.Wake += () => throw new InvalidOperationException("fake wake failure");
        Assert.True(h.Session.TryTakeHorizontal(120)); h.Session.Pump();
        Assert.Equal(new[] { -60 }, h.Input.WheelEvents);
    }

    [Fact]
    public async Task HookDoesNotWaitForGateHeldByHidSubmission()
    {
        var h = PendingApply(); h.AutoReply = true;
        h.Reply(h.Sent[^1]);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        h.Intercept = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return true; };
        var worker = Task.Run(h.Session.Pump);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var accepted = Task.Run(() => h.Session.TryTakeHorizontal(120));
            Assert.True(await accepted.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { release.Set(); await worker; }
        h.Session.Pump(); Assert.Equal(new[] { -60 }, h.Input.WheelEvents);
    }
}
