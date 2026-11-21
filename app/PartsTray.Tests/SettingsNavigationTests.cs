// アイコンから開く設定タブと欄を検証します。
// 設定画面の入口を増やしたときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class SettingsNavigationTests
{
    [Theory]
    [InlineData(SettingsIconKind.Parrot, "オウムを出す")]
    [InlineData(SettingsIconKind.Cpu, "CPU 使用率のアイコンを出す")]
    [InlineData(SettingsIconKind.Temperature, "温度の読み方")]
    [InlineData(SettingsIconKind.Memory, "メモリの色")]
    [InlineData(SettingsIconKind.Battery, "電池のアイコンを出す")]
    public void ResolvesIconTabAndField(SettingsIconKind kind, string field) =>
        Assert.Equal(new SettingsDestination("通知領域のアイコン", field), SettingsNavigation.Resolve(kind));

    [Fact]
    public void BatteryUsesExactDeviceName() =>
        Assert.Equal(new SettingsDestination("通知領域のアイコン", "機器の種類", "My Mouse"),
            SettingsNavigation.Resolve(SettingsIconKind.Battery, "My Mouse"));

    [Fact]
    public void EmptyBatteryNameUsesBatterySection() =>
        Assert.Equal("電池のアイコンを出す", SettingsNavigation.Resolve(SettingsIconKind.Battery, "").Field);

    [Theory]
    [InlineData(SettingsIconKind.Other)]
    [InlineData((SettingsIconKind)999)]
    public void OtherIconsKeepExistingDestination(SettingsIconKind kind) =>
        Assert.Equal(new SettingsDestination(), SettingsNavigation.Resolve(kind, "hidden window"));

    [Fact]
    public void DeviceNamesDoNotAffectMonitorNavigation() =>
        Assert.Equal(SettingsNavigation.Resolve(SettingsIconKind.Cpu), SettingsNavigation.Resolve(SettingsIconKind.Cpu, "mouse"));
}
