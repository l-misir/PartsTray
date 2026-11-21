// USB でつないだ Android などの電池の値の直し方（PortableBattery.cs）を検証します。
// 充電中の扱いや種類の見分け方を変えたときに実行する純粋なテストです。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class PortableBatteryTests
{
    [Fact]
    public void BatteryOnlyIsDischargingAndExternalPowerIsChargingOrFull()
    {
        var onBattery = PortableBattery.ToReading(57, 0)!;
        Assert.Equal(57, onBattery.Percent); Assert.Equal(BatteryCharge.Discharging, onBattery.Charge); Assert.False(onBattery.ExternalPower); Assert.Null(onBattery.Detail);
        var plugged = PortableBattery.ToReading(80, 1)!;
        Assert.Equal(BatteryCharge.Charging, plugged.Charge); Assert.True(plugged.ExternalPower); Assert.Equal(PortableBattery.ChargingNote, plugged.Detail);
        var full = PortableBattery.ToReading(100, 1)!;
        Assert.Equal(BatteryCharge.Full, full.Charge); Assert.Null(full.Detail);
        Assert.Equal(BatteryCharge.Discharging, PortableBattery.ToReading(30, null)!.Charge);   // 電源の種類を知らせない機器は電池とみなす
    }

    [Theory]
    [InlineData(null, null)] [InlineData(101u, 1u)] [InlineData(null, 1u)]
    public void MissingOrBadLevelsAreIgnored(uint? level, uint? source) => Assert.Null(PortableBattery.ToReading(level, source));

    [Theory]
    [InlineData(null, BatteryDeviceKind.Phone)] [InlineData(0u, BatteryDeviceKind.Phone)] [InlineData(3u, BatteryDeviceKind.Phone)]
    [InlineData(1u, BatteryDeviceKind.Other)] [InlineData(2u, BatteryDeviceKind.Other)]
    public void DeviceTypesMapToKinds(uint? type, BatteryDeviceKind kind) => Assert.Equal(kind, PortableBattery.Kind(type));

    [Fact]
    public void AppleDevicesAreLeftToTheAppleService()
    {
        Assert.True(PortableBattery.IsApple("Apple Inc.")); Assert.False(PortableBattery.IsApple("Google")); Assert.False(PortableBattery.IsApple(null));
    }

    [Fact]
    public void AndroidReadingIsOnByDefaultAndCanBeTurnedOff()
    {
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{}", AppSettings.JsonOptions)!.Battery.AndroidDevices);
        Assert.False(JsonSerializer.Deserialize<AppSettings>("{\"battery\":{\"androidDevices\":false}}", AppSettings.JsonOptions)!.Battery.AndroidDevices);
    }
}
