using System.Globalization;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class OptionsRestartTests
{
    private const string FreshReason = "Options+ は起動後まだ設定していない（前の横取りが残っている）";
    private static string Decision(Harness h) => h.Log.Lines.Last(l => l.StartsWith("接続判定: divert="));
    private static void StartNewAgent(Harness h)
    {
        h.UtcNow = h.UtcNow.AddMinutes(10);
        h.OptionsStartedUtc = h.UtcNow.AddSeconds(-5);
        h.Cids[0x52] = 1;
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(0)]
    public void AgentStartedBeforeOrAtSessionCreationIsAssumedConfigured(int minutes)
    {
        var h = new Harness(); h.OptionsStartedUtc = h.UtcNow.AddMinutes(minutes); h.Cids[0x52] = 1;
        h.Activate();

        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Empty(h.Writes);
        string local = h.OptionsStartedUtc.Value.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Assert.Equal($"接続判定: divert=1 Options+=True previousActive=False Options+起動={local} 起動後の設定=なし", Decision(h));
        Assert.Contains(h.Log.Lines, l => l.Contains("接続判定: Options+ 設定済み"));
    }

    [Fact]
    public void FreshAgentActivatesNotifiesAndReleasesRemainingThumbDiversion()
    {
        var h = new Harness(); StartNewAgent(h); h.Thumb = 1;
        var notices = new List<string>(); h.Session.SwitchNotice += notices.Add;
        h.Activate();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Contains(h.Log.Lines, l => l.Contains(FreshReason));
        Assert.Contains("起動後の設定=なし", Decision(h));
        Assert.StartsWith("肩代わりを開始しました", Assert.Single(notices));
        var release = Assert.Single(h.Writes, r => r[2] == h.Features[0x2150]);
        Assert.Equal(0x25, release[3]);
        Assert.Equal(new byte[] { 0, 0 }, release[4..6]);
        Assert.Equal(0, h.Thumb);
        Assert.Single(h.Log.Lines, l => l.StartsWith("適用完了"));

        h.Options();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Equal(2, notices.Count);
        Assert.StartsWith("Options+ に引き継ぎました", notices[1]);
    }

    [Theory]
    [InlineData(20, 3, false)]
    [InlineData(13, 3, false)]
    [InlineData(19, 2, false)]
    [InlineData(20, 3, true)]
    [InlineData(13, 3, true)]
    [InlineData(19, 2, true)]
    public void ConfigurationObservedWhileActiveOrRetreatedSurvivesReconnect(byte feature, byte function, bool retreated)
    {
        var h = new Harness();
        if (retreated) h.Cids[0x52] = 1;
        h.Activate();
        Assert.Equal(retreated ? SessionState.Retreated : SessionState.Active, h.Session.State);
        StartNewAgent(h);
        h.Options(feature, function);
        Assert.Equal(SessionState.Retreated, h.Session.State);
        h.Session.ReceiverLost();
        h.Sent.Clear(); h.Link(); h.Session.Pump();

        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Empty(h.Writes);
        Assert.Contains("起動後の設定=あり", Decision(h));
        Assert.Contains(h.Log.Lines, l => l.Contains("接続判定: Options+ 設定済み"));
    }

    [Fact]
    public void AnotherRestartAfterObservedConfigurationActivatesAgain()
    {
        var h = new Harness(); StartNewAgent(h); h.Activate(); h.Options();
        h.Link(); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Contains("起動後の設定=あり", Decision(h));

        StartNewAgent(h); h.Sent.Clear(); h.Link(); h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.NotEmpty(h.Writes);
        Assert.Contains("起動後の設定=なし", Decision(h));
        Assert.Contains(h.Log.Lines, l => l.Contains(FreshReason));
    }

    [Fact]
    public void ConfigurationAtExactStartTimeIsAlreadyObserved()
    {
        var h = new Harness(); h.Cids[0x52] = 1; h.Activate();
        StartNewAgent(h); h.OptionsStartedUtc = h.UtcNow;
        h.Options(); h.Sent.Clear(); h.Link(); h.Session.Pump();

        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Empty(h.Writes);
        Assert.Contains("起動後の設定=あり", Decision(h));
    }

    [Fact]
    public void UnknownStartTimeRetreatsEvenAfterEarlierConfiguration()
    {
        var h = new Harness(); h.Cids[0x52] = 1; h.Activate(); h.Options();
        h.Sent.Clear(); h.Link(); h.Session.Pump(); h.Advance(30);

        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Empty(h.Writes);
        Assert.Contains("Options+起動=不明 起動後の設定=なし", Decision(h));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrThrowingProviderPreservesLegacyRetreat(bool throws)
    {
        var h = new Harness { AutoReply = false };
        int calls = 0;
        Func<DateTime?>? provider = throws ? () => { calls++; throw new InvalidOperationException("fake start failure"); } : null;
        var session = new GapSession(h, h.Input, h.Clock, h.Log, () => true, new() { GapFiller = new() { Enabled = true } },
            optionsStartedUtc: provider, utcNow: () => h.UtcNow);
        session.Receive([0x10, 2, 0x41, 0x10, 2, 0x42, 0xB0]);
        for (int i = 0; i < 4; i++)
        {
            session.Pump();
            var request = HidReport.Parse(h.Sent[^1])!;
            session.Receive(Harness.Frame(2, 0, 0, 5, h.Features[HidReport.U16(request.Parameters)], 0, 1));
        }
        session.Pump();
        var exception = Record.Exception(() => session.Receive(Harness.Frame(2, 13, 2, 5, 0, 0x52, 1)));
        session.Pump();

        Assert.Null(exception);
        Assert.Equal(throws ? 1 : 0, calls);
        Assert.Equal(SessionState.Retreated, session.State);
        Assert.Empty(h.Writes);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("エラー"));
        if (throws) Assert.Contains("Options+起動=不明 起動後の設定=なし", Decision(h));
        else
        {
            Assert.DoesNotContain("Options+起動=", Decision(h));
            Assert.DoesNotContain("起動後の設定=", Decision(h));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-60)]
    [InlineData(10)]
    public void StoppedOptionsActivatesWithoutStartTimeFields(int? minutes)
    {
        var h = new Harness { OptionsRunning = false };
        h.OptionsStartedUtc = minutes.HasValue ? h.UtcNow.AddMinutes(minutes.Value) : null;
        h.Cids[0x52] = 1; h.Activate();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.NotEmpty(h.Writes);
        Assert.DoesNotContain("Options+起動=", Decision(h));
        Assert.DoesNotContain("起動後の設定=", Decision(h));
    }

    [Fact]
    public void PreviousActiveSkipsStartTimeDecision()
    {
        var h = new Harness(); h.Activate(); StartNewAgent(h);
        h.Sent.Clear(); h.Link(); h.Session.Pump();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.NotEmpty(h.Writes);
        Assert.Equal("接続判定: divert=1 Options+=True previousActive=True", Decision(h));
    }

    [Fact]
    public void NoDiversionSkipsStartTimeDecision()
    {
        var h = new Harness(); StartNewAgent(h); h.Cids[0x52] = 0; h.Activate();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal("接続判定: divert=0 Options+=True previousActive=False", Decision(h));
    }

    [Theory]
    [InlineData(0, 20, 3)]
    [InlineData(5, 20, 3)]
    [InlineData(11, 20, 2)]
    [InlineData(11, 13, 2)]
    [InlineData(11, 19, 1)]
    public void NotificationsOwnResponsesAndReadsDoNotCountAsConfiguration(byte swid, byte feature, byte function)
    {
        var h = new Harness(); h.Cids[0x52] = 1; h.Activate();
        StartNewAgent(h); h.OptionsFrom(swid, feature, function);
        h.Link(); h.Session.Pump();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Contains("起動後の設定=なし", Decision(h));
    }
}
