// ACPI の下限・固定値判定・復帰と通知回数を検証します。
// 温度判定の条件を変えたときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class AcpiTemperatureHealthTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(19.99)]
    [InlineData(0)]
    [InlineData(-10)]
    public void AcpiRejectsTemperaturesBelowTwenty(double celsius) =>
        Assert.Null(MonitorMath.FromAcpi([celsius + 273.15]));

    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(149)]
    public void AcpiKeepsValidTemperatures(double celsius) =>
        Assert.Equal(celsius, MonitorMath.FromAcpi([289.15, celsius + 273.15])!.Value.Average, 8);

    [Fact]
    public void RejectsAtFiveMinutesAndThirtyPointsAndRecoversOnChange()
    {
        TimeSpan now = TimeSpan.Zero;
        var health = new AcpiTemperatureHealth(() => now);
        CpuTemperature value = new(40, 40);
        Assert.Equal(value, health.Observe("acpi", value, 10));
        now = TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1);
        Assert.Equal(value, health.Observe("acpi", value, 40));
        now += TimeSpan.FromMilliseconds(1);
        Assert.Null(health.Observe("acpi", value, 20));
        Assert.True(health.IsUnavailable);
        Assert.True(health.TakeNotice());
        Assert.False(health.TakeNotice());
        Assert.Null(health.Observe("acpi", null, 20));
        Assert.Null(health.Observe("acpi", value, 20)); // 読取失敗だけでは固定値判定を解除しない
        var changed = new CpuTemperature(41, 41);
        Assert.Equal(changed, health.Observe("acpi", changed, 10));
        Assert.False(health.IsUnavailable);
        now += TimeSpan.FromMinutes(5);
        Assert.Null(health.Observe("acpi", changed, 70));
        Assert.False(health.TakeNotice()); // 再発でも一度だけ
    }

    [Theory]
    [InlineData(10)]
    [InlineData(39.99)]
    public void StableUsageDoesNotInvalidateTemperature(double laterUsage)
    {
        TimeSpan now = TimeSpan.Zero;
        var health = new AcpiTemperatureHealth(() => now);
        CpuTemperature value = new(40, 40);
        health.Observe("acpi", value, 10);
        now = TimeSpan.FromHours(1);
        Assert.Equal(value, health.Observe("acpi", value, laterUsage));
        Assert.False(health.IsUnavailable);
        Assert.False(health.TakeNotice());
    }

    [Fact]
    public void ChangingTemperatureRestartsObservationAndUsageRange()
    {
        TimeSpan now = TimeSpan.Zero;
        var health = new AcpiTemperatureHealth(() => now);
        health.Observe("acpi", new(40, 40), 0);
        now = TimeSpan.FromMinutes(4);
        health.Observe("acpi", new(40, 40), 100);
        now += TimeSpan.FromSeconds(1);
        var changed = new CpuTemperature(41, 41);
        health.Observe("acpi", changed, 50);
        now += TimeSpan.FromMinutes(5);
        Assert.Equal(changed, health.Observe("acpi", changed, 50));
    }

    [Fact]
    public void MissingReadingBreaksObservationAndRecovers()
    {
        TimeSpan now = TimeSpan.Zero;
        var health = new AcpiTemperatureHealth(() => now);
        CpuTemperature value = new(40, 40);
        health.Observe("acpi", value, 0);
        now = TimeSpan.FromMinutes(4);
        Assert.Null(health.Observe("acpi", null, 100));
        Assert.True(health.TakeNotice());
        now += TimeSpan.FromMinutes(2);
        Assert.Equal(value, health.Observe("acpi", value, 100));
        Assert.False(health.IsUnavailable);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(101d)]
    [InlineData(null)]
    public void InvalidCpuSamplesDoNotCreateVariation(double? usage)
    {
        TimeSpan now = TimeSpan.Zero;
        var health = new AcpiTemperatureHealth(() => now);
        CpuTemperature value = new(40, 40);
        health.Observe("acpi", value, 50);
        now = TimeSpan.FromMinutes(5);
        Assert.Equal(value, health.Observe("acpi", value, usage));
    }

    [Fact]
    public void CoreTempBypassesClockAndAcpiLowerLimit()
    {
        var health = new AcpiTemperatureHealth(() => throw new InvalidOperationException("ACPI のときだけ時計を使う"));
        CpuTemperature value = new(16, 16);
        Assert.Equal(value, health.Observe("coretemp", value, 0));
        Assert.Equal(value, health.Observe("coretemp", value, 100));
        Assert.Null(health.Observe("coretemp", null, 100));
        Assert.False(health.IsUnavailable);
        Assert.False(health.TakeNotice());
    }

    [Fact]
    public void SwitchingSourcesStartsNewAcpiObservation()
    {
        TimeSpan now = TimeSpan.Zero;
        var health = new AcpiTemperatureHealth(() => now);
        CpuTemperature value = new(40, 40);
        health.Observe("acpi", value, 0);
        now = TimeSpan.FromMinutes(5);
        Assert.Null(health.Observe("acpi", value, 100));
        Assert.Equal(value, health.Observe("coretemp", value, 100));
        Assert.Equal(value, health.Observe("acpi", value, 100));
        Assert.False(health.IsUnavailable);
    }

    [Fact]
    public void ResetStartsNewObservationWithoutRepeatingNotice()
    {
        TimeSpan now = TimeSpan.Zero;
        var health = new AcpiTemperatureHealth(() => now);
        health.Observe("acpi", null, 0);
        Assert.True(health.TakeNotice());
        health.Reset();
        Assert.False(health.IsUnavailable);
        Assert.Equal(new CpuTemperature(40, 40), health.Observe("acpi", new(40, 40), 100));
        health.Observe("acpi", null, 0);
        Assert.False(health.TakeNotice());
    }
}
