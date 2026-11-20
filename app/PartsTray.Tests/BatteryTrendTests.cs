// 残量の増え方から充電中を推測する決まり（BatteryTrend.cs）を検証します。
// 推測の条件やみなす時間を変えたときに実行する純粋なテストです。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class BatteryTrendTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstReadingIsUnknownAndRiseMeansCharging()
    {
        var t = new BatteryTrend();
        Assert.Equal(BatteryCharge.Unknown, t.Update("h", 50, true, T0));
        Assert.Equal(BatteryCharge.Charging, t.Update("h", 52, true, T0.AddMinutes(1)));
    }

    [Fact]
    public void SameLevelKeepsChargingOnlyForTwentyMinutesAfterTheLastRise()
    {
        var t = new BatteryTrend();
        t.Update("h", 50, true, T0); t.Update("h", 51, true, T0.AddMinutes(1));
        Assert.Equal(BatteryCharge.Charging, t.Update("h", 51, true, T0.AddMinutes(20)));
        Assert.Equal(BatteryCharge.Unknown, t.Update("h", 51, true, T0.AddMinutes(21)));
    }

    [Fact]
    public void DropEndsChargingAndHundredIsFull()
    {
        var t = new BatteryTrend();
        t.Update("h", 98, true, T0);
        Assert.Equal(BatteryCharge.Full, t.Update("h", 100, true, T0.AddMinutes(1)));
        Assert.Equal(BatteryCharge.Unknown, t.Update("h", 99, true, T0.AddMinutes(2)));
    }

    [Fact]
    public void DisconnectForgetsTheLevelSoChargingWhileOffIsNotMistaken()
    {
        // 切れている間に充電して戻ってきた（60% → 100%）ときは、充電中とみなさない。
        var t = new BatteryTrend();
        t.Update("h", 60, true, T0);
        Assert.Equal(BatteryCharge.Unknown, t.Update("h", 60, false, T0.AddHours(1)));
        Assert.Equal(BatteryCharge.Unknown, t.Update("h", 100, true, T0.AddHours(8)));
    }

    [Fact]
    public void DevicesAreTrackedSeparately()
    {
        var t = new BatteryTrend();
        t.Update("a", 50, true, T0); t.Update("b", 80, true, T0);
        Assert.Equal(BatteryCharge.Charging, t.Update("a", 55, true, T0.AddMinutes(1)));
        Assert.Equal(BatteryCharge.Unknown, t.Update("b", 79, true, T0.AddMinutes(1)));
    }

    [Fact]
    public void TooltipShowsTheGuessNote()
    {
        var reading = new BatteryReading(60, BatteryLevel.Unknown, BatteryCharge.Charging, false, false, BatteryTrend.Note);
        var device = new BatteryDevice("bluetooth:h", "WH-1000XM6", BatteryDeviceKind.Headphones, true, reading, T0);
        Assert.Equal("WH-1000XM6 60%（充電中）\n残量が増えたので、充電中とみなしています", BatteryMath.Tooltip(device));
        Assert.True(BatteryMath.ShowsBolt(reading));
    }
}
