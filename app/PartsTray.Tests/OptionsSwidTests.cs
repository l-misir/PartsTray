using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

// 指示書F：Options+ の swid は起動ごとに変わる（0xA・0xB・0xC・0xE を実機で確認）。
public sealed class OptionsSwidTests
{
    private static (Harness H, List<string> Notices) Active()
    {
        var h = new Harness(); var notices = new List<string>();
        h.Session.SwitchNotice += notices.Add;
        h.Activate();
        Assert.Equal(SessionState.Active, h.Session.State);
        return (h, notices);
    }

    [Theory]
    [InlineData(0x1)]
    [InlineData(0xA)]
    [InlineData(0xB)]
    [InlineData(0xF)]
    public void DpiWriteFromAnyOtherSwidRetreats(int swid)
    {
        var (h, notices) = Active();
        h.OptionsFrom((byte)swid); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.StartsWith("Options+ に引き継ぎました", notices.Last());
        Assert.Contains(h.Log.Lines, l => l.Contains($"Options+ 設定開始（swid={swid:X}）: setSensorDpi"));
    }

    [Fact]
    public void CidWriteFromSwidBRetreats()
    {
        var (h, _) = Active();
        h.OptionsFrom(0xB, 13, 3, 0x00, 0x52, 0x23); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Equal(0x23, h.Session.OptionsFlags[0x52]);
    }

    [Fact]
    public void ThumbWriteFromSwidBRetreatsAndEndsBridge()
    {
        var (h, _) = Active();
        Assert.True(h.Session.TryTakeHorizontal(120));
        h.OptionsFrom(0xB, 19, 2); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Contains(h.Log.Lines, l => l.Contains("橋渡しを終了（Options+ がサムホイールを設定）"));
        Assert.False(h.Session.TryTakeHorizontal(120));
    }

    [Fact]
    public void OwnSwidResponseWithoutPendingRequestDoesNotRetreat()
    {
        var (h, _) = Active();
        h.OptionsFrom(5); h.OptionsFrom(5, 13, 3, 0x00, 0x52, 0x03); h.OptionsFrom(5, 19, 2); h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public void NotificationsDoNotRetreat()
    {
        var (h, _) = Active();
        h.Session.Receive(Harness.Frame(2, 19, 0, 0, 0x00, 0x05, 0x00, 0x07, 0x02, 0x02));
        h.Buttons(0x53); h.Buttons();
        h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public void OtherFunctionsFromSwidBDoNotRetreat()
    {
        var (h, _) = Active();
        h.OptionsFrom(0xB, 19, 1, 0x01, 0x03); // getThumbwheelStatus
        h.OptionsFrom(0xB, 0, 1, 0x04, 0x05);  // ROOT ping
        h.OptionsFrom(0xB, 13, 2, 0x00, 0x52); // getCidReporting
        h.Session.Pump();
        Assert.Equal(SessionState.Active, h.Session.State);
    }

    [Fact]
    public void EarlyWriteFromSwidBDuringDiscoveryRetreatsWithoutWrites()
    {
        var h = new Harness(); var notices = new List<string>(); h.Session.SwitchNotice += notices.Add;
        h.Link(); h.OptionsFrom(0xB); h.Session.Pump();
        Assert.Equal(SessionState.Retreated, h.Session.State);
        Assert.Empty(h.Writes);
        Assert.Empty(notices);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(11)]
    public void LegacyOptionsPlusSwidKeyIsIgnored(int swid)
    {
        var config = JsonSerializer.Deserialize<AppSettings>($"{{\"gapFiller\":{{\"optionsPlusSwid\":{swid}}}}}", AppSettings.JsonOptions)!;
        config.Validate();
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(config, AppSettings.JsonOptions));
        Assert.False(saved.RootElement.GetProperty("gapFiller").TryGetProperty("optionsPlusSwid", out _));
    }
}
