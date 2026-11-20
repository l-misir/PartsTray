using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class RemainingThumbDiversionTests
{
    private static Harness Create(string mode = "hook") =>
        new(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = mode } } });

    [Fact]
    public void HookReadsThenReleasesRemainingDiversionOnce()
    {
        var h = Create(); h.Thumb = 1;
        h.Activate(); h.Session.Pump();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Collection(h.Sent.Where(r => r[2] == h.Features[0x2150]),
            r => Assert.Equal(0x15, r[3]),
            r =>
            {
                Assert.Equal(0x25, r[3]);
                Assert.Equal(new byte[] { 0, 0 }, r[4..6]);
            });
        Assert.Equal(0, h.Thumb);
        Assert.Contains("サムホイール: 横取りが残っていたので解除する（01 01）", h.Log.Lines);
        Assert.Single(h.Log.Lines, l => l.StartsWith("適用完了"));
        Assert.True(h.Session.TryTakeHorizontal(120));
    }

    [Fact]
    public void HookReadsWithoutWritingWhenNoDiversionRemains()
    {
        var h = Create(); h.Thumb = 0;
        h.Activate(); h.Session.Pump();

        var read = Assert.Single(h.Sent, r => r[2] == h.Features[0x2150]);
        Assert.Equal(0x15, read[3]);
        Assert.DoesNotContain(h.Writes, r => r[2] == h.Features[0x2150]);
        Assert.Contains("サムホイール: 横取りは残っていない（00 00）", h.Log.Lines);
        Assert.Single(h.Log.Lines, l => l.StartsWith("適用完了"));
    }

    [Fact]
    public void OptionsBeforeStatusReplyPreventsReleaseFromLateReply()
    {
        var h = Create(); h.Thumb = 1;
        h.Intercept = r =>
        {
            if (r[2] == h.Features[0x2150] && r[3] == 0x15)
            {
                h.Options();
                Assert.Equal(SessionState.Retreated, h.Session.State);
            }
            return false; // Harness delivers the status reply after Options+ has taken over.
        };
        h.Activate(); h.Session.Pump();

        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Single(h.Sent, r => r[2] == h.Features[0x2150] && r[3] == 0x15);
        Assert.DoesNotContain(h.Writes, r => r[2] == h.Features[0x2150]);
        Assert.Equal(1, h.Thumb);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("横取りが残っていたので解除する") || l.StartsWith("適用完了"));
    }

    [Fact]
    public void OptionsAfterStatusReplyButBeforeReleasePreventsWrite()
    {
        var h = Create(); h.Thumb = 1;
        h.Intercept = r =>
        {
            if (r[2] != h.Features[0x2150] || r[3] != 0x15) return false;
            h.Reply(r);
            Assert.Contains("サムホイール: 横取りが残っていたので解除する（01 01）", h.Log.Lines);
            Assert.DoesNotContain(h.Writes, sent => sent[2] == h.Features[0x2150]);
            h.Options();
            return true;
        };
        h.Activate(); h.Session.Pump();

        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.DoesNotContain(h.Writes, r => r[2] == h.Features[0x2150]);
        Assert.Equal(1, h.Thumb);
        Assert.DoesNotContain(h.Log.Lines, l => l.StartsWith("適用完了"));
    }

    [Fact]
    public void OffLeavesRemainingDiversionUntouched()
    {
        var h = Create("off"); h.Thumb = 1;
        h.Activate();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.DoesNotContain(h.Sent, r => r[2] == h.Features[0x2150]);
        Assert.Equal(1, h.Thumb);
        Assert.Single(h.Log.Lines, l => l.StartsWith("適用完了"));
        Assert.False(h.Session.TryTakeHorizontal(120));
    }

    [Fact]
    public void HookReleaseDoesNotOwnThumbForLaterCleanup()
    {
        var config = new AppSettings { GapFiller = new() { Enabled = true } };
        var h = new Harness(config); h.Thumb = 1;
        h.Activate();
        Assert.Equal(0, h.Thumb);
        Assert.Single(h.Writes, r => r[2] == h.Features[0x2150]);
        int beforeCleanup = h.Sent.Count;
        h.Options();
        h.Advance(config.GapFiller.CleanupDelaySeconds + 1);

        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Contains(h.Log.Lines, l => l.StartsWith("後片付け開始"));
        Assert.DoesNotContain(h.Sent.Skip(beforeCleanup), r => r[2] == h.Features[0x2150]);
        Assert.Equal(0, h.Thumb);
    }

    [Fact]
    public void DivertApplicationStillWritesOnlyOnThenReadsBack()
    {
        var h = Create("divert"); h.Activate();

        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Collection(h.Sent.Where(r => r[2] == h.Features[0x2150]),
            r =>
            {
                Assert.Equal(0x25, r[3]);
                Assert.Equal(new byte[] { 1, 1 }, r[4..6]);
            },
            r => Assert.Equal(0x15, r[3]));
        Assert.DoesNotContain(h.Writes, r => r[2] == h.Features[0x2150] && r[4] == 0 && r[5] == 0);
        Assert.Equal(1, h.Thumb);
        Assert.Single(h.Log.Lines, l => l.StartsWith("適用完了"));
    }

    [Theory]
    [InlineData(0, 2, false)]
    [InlineData(2, 1, false)]
    [InlineData(3, 2, true)]
    public void HookUsesOnlyDiversionBitOfFirstStatusByte(byte reporting, byte status, bool release)
    {
        var h = Create(); h.Thumb = reporting;
        h.Intercept = r =>
        {
            if (r[2] != h.Features[0x2150] || r[3] != 0x15) return false;
            h.Session.Receive(Harness.Frame(2, h.Features[0x2150], 1, 5, reporting, status));
            return true;
        };
        h.Activate();

        Assert.Equal(release ? 1 : 0, h.Writes.Count(r => r[2] == h.Features[0x2150]));
        Assert.Equal(release ? (byte)0 : reporting, h.Thumb);
        string message = release ? "横取りが残っていたので解除する" : "横取りは残っていない";
        Assert.Contains($"サムホイール: {message}（{reporting:X2} {status:X2}）", h.Log.Lines);
        Assert.Single(h.Log.Lines, l => l.StartsWith("適用完了"));
    }
}
