using System.Globalization;
using System.Text.RegularExpressions;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class ReplayTests
{
    [Theory]
    [InlineData("sniff_20261118_224803.log", 22.656, 23.539)]
    [InlineData("sniff_20261118_230321.log", 1.129, 17.455)]
    public void RealLogReplayAppliesAndRetreatsAtExpectedTimes(string file, double linkTime, double retreatTime)
    {
        var h = new Harness(new() { GapFiller = new() { Enabled = true, ThumbWheel = new() { Mode = "divert" } } }); bool sawLink = false, sawRetreat = false;
        bool sawCopy = false, sawPaste = false; int reports = 0;
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)))
        {
            if (line.Contains("レシーバーが外されました") || line.Contains("レシーバー検出:"))
            { h.Session.ReceiverLost(); h.Clock.Now = TimeSpan.Zero; }
            var m = Regex.Match(line, @"\+\s*([\d.]+)s.*\|\s*((?:[0-9a-fA-F]{2}\s*)+)$");
            if (!m.Success) continue;
            reports++;
            double seconds = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            h.Clock.Now = TimeSpan.FromSeconds(seconds);
            byte[] raw = Convert.FromHexString(Regex.Replace(m.Groups[2].Value, @"\s", ""));
            var report = HidReport.Parse(raw);
            // Both captures show the mouse losing its settings on link establishment.
            if (report is { Kind: ReportKind.Connection, Wpid: 0xB042, LinkUp: true }) h.Cids[0x52] = 0;
            int keysBefore = h.Input.KeyEvents.Count, wheelBefore = h.Input.WheelEvents.Count;
            h.Session.Receive(raw);
            if (!sawLink && seconds == linkTime)
            {
                Assert.Equal(SessionState.Retreated, h.Session.State); // No input until the reporting query completes.
                int before = h.Writes.Length; h.Session.Pump();
                Assert.Equal(SessionState.Active, h.Session.State);
                var writes = h.Writes.Skip(before).ToArray(); Assert.Equal(7, writes.Length);
                Assert.Equal(20, writes[0][2]);
                Assert.Equal(GapSession.ButtonOrder, writes.Skip(1).Take(5).Select(r => HidReport.U16(r.AsSpan(4, 2))));
                Assert.Equal(19, writes[6][2]); sawLink = true;
            }
            if (!sawRetreat && seconds == retreatTime && sawLink)
            { Assert.Equal(SessionState.Retreated, h.Session.State); sawRetreat = true; }
            h.Session.Pump();
            if (h.Session.State != SessionState.Active)
            {
                Assert.Equal(keysBefore, h.Input.KeyEvents.Count);
                Assert.Equal(wheelBefore, h.Input.WheelEvents.Count);
            }
            if (file.Contains("230321") && seconds is 24.168 or 25.743)
            {
                Assert.Equal(SessionState.Retreated, h.Session.State); Assert.Empty(h.Input.KeyEvents);
                if (seconds == 24.168) sawCopy = true; else sawPaste = true;
            }
        }
        Assert.True(sawLink); Assert.True(sawRetreat); Assert.True(reports > 100);
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents);
        if (file.Contains("230321")) { Assert.True(sawCopy); Assert.True(sawPaste); }
        Assert.All(h.Writes, r => { Assert.Equal(2, r[1]); Assert.Equal(5, r[3] & 15); });
    }
}
