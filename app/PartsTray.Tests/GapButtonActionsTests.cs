// 6 ボタンの横取り・任意キー・Hold と引き継ぎを偽の機器で検証します。
// 適用順序・押下通知・二重送信防止・後片付けを変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class GapButtonActionsTests
{
    private static AppSettings Config(string action = "Hold", string? keys = null) => new()
    {
        GapFiller = SettingsText.SetButton(new() { Enabled = true }, "0x00C4", action, keys)
    };

    [Theory]
    [InlineData("off", false)] [InlineData("off", true)]
    [InlineData("hook", false)] [InlineData("hook", true)]
    [InlineData("divert", false)] [InlineData("divert", true)]
    public void RearButtonIsWrittenOnlyWhenConfiguredAndThumbStillFollows(string mode, bool includeRear)
    {
        var gap = new GapSettings { Enabled = true, ThumbWheel = new() { Mode = mode } };
        if (includeRear) gap = SettingsText.SetButton(gap, "0x00C4", "Hold");
        var h = new Harness(new() { GapFiller = gap }) { Thumb = 1 };
        h.Activate();
        ushort[] expected = includeRear ? [0x52, 0xC4, 0x56, 0x53, 0xC3, 0x1A0] : [0x52, 0x56, 0x53, 0xC3, 0x1A0];
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.Equal(expected.Length + (mode == "off" ? 1 : 2), h.Writes.Length);
        Assert.Equal(20, h.Writes[0][2]);
        Assert.Equal(expected, h.Writes.Skip(1).Take(expected.Length).Select(r => HidReport.U16(r.AsSpan(4))));
        Assert.All(h.Writes.Skip(1).Take(expected.Length), r => Assert.Equal(3, r[6]));
        if (mode != "off")
        {
            Assert.Equal(19, h.Writes[^1][2]);
            Assert.Equal(mode == "hook" ? new byte[] { 0, 0 } : new byte[] { 1, 1 }, h.Writes[^1][4..6]);
        }
        Assert.Contains(h.Log.Lines, line => line.StartsWith("適用完了"));
        h.Options(); h.Sent.Clear(); h.Advance(15);
        Assert.Equal(expected, h.Writes.Where(r => r[2] == 13).Select(r => HidReport.U16(r.AsSpan(4))));
        Assert.All(h.Writes.Where(r => r[2] == 13), r => Assert.Equal(2, r[6]));
        Assert.Equal(expected.Length + (mode == "divert" ? 1 : 0), h.Writes.Length);
        int count = h.Sent.Count; h.Advance(60); Assert.Equal(count, h.Sent.Count);
    }

    [Fact]
    public void ArbitraryKeysSendOncePerPressAndStopAtHandoff()
    {
        const string keys = "Ctrl+Alt+Shift+F12";
        var h = new Harness(Config("Keys", keys)); h.Activate();
        h.Buttons(0xC4); Assert.Empty(h.Input.KeyEvents); h.Session.Pump();
        h.Buttons(0xC4); h.Session.Pump();
        Assert.Equal(new[] { keys }, h.Input.KeyEvents);
        h.Buttons(); h.Buttons(0xC4); h.Session.Pump();
        Assert.Equal(new[] { keys, keys }, h.Input.KeyEvents);
        Assert.Equal(new ushort[] { 0x11, 0x12, 0x10, 0x7B, 0x7B, 0x10, 0x12, 0x11 },
            InputSequence.ForKeys(keys).Select(k => k.VirtualKey));
        Assert.Equal(new uint[] { 0, 0, 0, 0, 2, 2, 2, 2 }, InputSequence.ForKeys(keys).Select(k => k.Flags));
        h.Buttons(); h.Buttons(0xC4); // 未実行の押下も引き継ぎで破棄する。
        h.Options(13, 3, 0, 0xC4, 0x23); h.Session.Pump();
        h.Buttons(); h.Buttons(0xC4); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Equal(2, h.Input.KeyEvents.Count);
        h.Sent.Clear(); h.Advance(15);
        Assert.DoesNotContain(h.Sent, r => r[2] == 13 && r[5] == 0xC4);
    }

    [Theory]
    [InlineData(0x52)] [InlineData(0xC4)]
    public void OwnedHoldSendsNothingAndNotifiesWheelSelection(int cid)
    {
        string name = $"0x{cid:X4}";
        var config = new AppSettings
        {
            GapFiller = SettingsText.SetButton(new() { Enabled = true }, name, "Hold"),
            WindowKeys = new() { HoldButton = name, WheelSwitchMs = 0 }
        };
        var h = new Harness(config); h.Activate(); h.Sent.Clear();
        var wheel = new WindowSwitchWheel(() => h.Clock.Now);
        var hold = new WindowHoldSelection(wheel, () => h.Clock.Now); hold.Configure(config.WindowKeys);
        var notices = new List<IReadOnlyList<ushort>>();
        h.Session.PressedButtonsChanged += buttons => { notices.Add(buttons); hold.ReceiveButtons(buttons); };
        Assert.False(wheel.Receive(false, -120).Handled);
        h.Buttons((ushort)cid); h.Session.Pump();
        Assert.True(hold.IsActive); Assert.Equal(default, wheel.Summary);
        Assert.Equal(1, wheel.Receive(false, -120).Steps);
        h.Buttons((ushort)cid); h.Session.Pump();
        Assert.True(hold.IsActive);
        h.Buttons(); h.Session.Pump();
        Assert.False(hold.IsActive); Assert.False(wheel.Receive(false, -120).Handled);
        Assert.Equal(3, notices.Count); Assert.Equal(new[] { (ushort)cid }, notices[0]); Assert.Empty(notices[2]);
        Assert.Empty(h.Input.KeyEvents); Assert.Empty(h.Input.WheelEvents); Assert.Empty(h.Sent);
    }

    [Fact]
    public void RearCleanupIsCancelledWhenOptionsClaimsItAfterRead()
    {
        var h = new Harness(Config()); h.Activate(); h.Options();
        foreach (ushort cid in GapSession.ButtonOrder.Where(cid => cid != 0xC4))
            h.Options(13, 3, (byte)(cid >> 8), (byte)cid, 0x23);
        h.Intercept = raw =>
        {
            if (raw[2] != 13 || raw[3] != 0x25 || raw[5] != 0xC4) return false;
            h.Reply(raw); h.Options(13, 3, 0, 0xC4, 0x23); return true;
        };
        h.Sent.Clear(); h.Advance(15);
        Assert.Single(h.Sent); Assert.Empty(h.Writes);
    }

    [Fact]
    public void SubmittedRearDiversionWithoutAckIsStillCleanedAfterHandoff()
    {
        var h = new Harness(Config());
        h.Intercept = raw =>
        {
            if (raw[2] != 13 || raw[3] != 0x35 || raw[5] != 0xC4) return false;
            h.Cids[0xC4] = 1; h.Options(); return true;
        };
        h.Activate();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Equal(3, h.Writes.Length); // DPI・中ボタン・後ろのボタンまでで止まる。
        h.Intercept = null; h.Sent.Clear(); h.Advance(15);
        Assert.Equal(new ushort[] { 0x52, 0xC4 }, h.Writes.Select(r => HidReport.U16(r.AsSpan(4))));
        Assert.All(h.Writes, r => Assert.Equal(2, r[6]));
    }
}
