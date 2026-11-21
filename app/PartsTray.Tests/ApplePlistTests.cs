// iPhone・iPad とのやり取りに使う plist の組み立て・読み解きと、電池の値への変換（ApplePlist.cs）を検証します。
// 形式や電池の読み方を変えたときに実行する純粋なテストです。
using System.Text;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class ApplePlistTests
{
    [Fact]
    public void RoundTripsEveryKindOfValue()
    {
        var data = new Dictionary<string, object?>
        {
            ["Request"] = "GetValue", ["Port"] = 32498L, ["Ok"] = true, ["No"] = false, ["Bytes"] = new byte[] { 1, 2, 3 },
            ["List"] = new List<object?> { "a", 2L }, ["Inner"] = new Dictionary<string, object?> { ["X"] = "<&>" }
        };
        var back = ApplePlist.Read(Encoding.UTF8.GetBytes(ApplePlist.Write(data)));
        Assert.Equal("GetValue", back["Request"]); Assert.Equal(32498L, back["Port"]);
        Assert.Equal(true, back["Ok"]); Assert.Equal(false, back["No"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, back["Bytes"]);
        Assert.Equal(new List<object?> { "a", 2L }, back["List"]);
        Assert.Equal("<&>", ((Dictionary<string, object?>)back["Inner"]!)["X"]);
    }

    [Fact]
    public void BinaryPlistIsRejected() =>
        Assert.Throws<NotSupportedException>(() => ApplePlist.Read(Encoding.ASCII.GetBytes("bplist00xxxx")));

    // 11/20 実機で返ってきた値の形（iPhone：97%・充電中・電源あり）。
    private static Dictionary<string, object?> Battery(long percent, bool charging, bool full, bool external) => new()
    {
        ["BatteryCurrentCapacity"] = percent, ["BatteryIsCharging"] = charging, ["FullyCharged"] = full,
        ["ExternalConnected"] = external, ["ExternalChargeCapable"] = false, ["HasBattery"] = true
    };

    [Theory]
    [InlineData(97, true, false, true, BatteryCharge.Charging, true)]
    [InlineData(100, false, true, true, BatteryCharge.Full, true)]
    [InlineData(80, false, false, true, BatteryCharge.Unknown, true)]      // 電源につながっているが充電していない（いたわり充電など）
    [InlineData(55, false, false, false, BatteryCharge.Discharging, false)]
    public void BatteryValues(long percent, bool charging, bool full, bool external, BatteryCharge expected, bool power)
    {
        var r = ApplePlist.Battery(Battery(percent, charging, full, external))!;
        Assert.Equal((int)percent, r.Percent); Assert.Equal(expected, r.Charge); Assert.Equal(power, r.ExternalPower);
    }

    [Fact]
    public void MissingOrBadCapacityIsNotABattery()
    {
        Assert.Null(ApplePlist.Battery(new Dictionary<string, object?>()));
        Assert.Null(ApplePlist.Battery(new Dictionary<string, object?> { ["BatteryCurrentCapacity"] = 120L }));
    }
}
