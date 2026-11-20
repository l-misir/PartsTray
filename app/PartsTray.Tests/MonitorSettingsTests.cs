using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class MonitorSettingsTests
{
    [Fact]
    public void DefaultsRemainCompatibleWithOldJson()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", AppSettings.JsonOptions)!;
        settings.Validate();
        var monitor = settings.Monitor;
        Assert.True(monitor.Enabled); Assert.Equal(200, monitor.SampleIntervalMs);
        // 指示書L：CPU を独立アイコンへ移したので、オウムの既定は数字なしに変更。
        Assert.True(monitor.Parrot.Enabled); Assert.True(monitor.Parrot.Rainbow); Assert.Equal("none", monitor.Parrot.CpuText);
        Assert.True(monitor.Cpu.Enabled); Assert.Equal("#64D2FF", monitor.Cpu.Color);
        Assert.True(monitor.TempMemory.Split);
        Assert.Equal("#FFFFFF", monitor.Parrot.CpuTextColor);
        Assert.True(monitor.Parrot.Stretch);   // 11/20：オウムは縦に伸ばして枠いっぱいにする
        Assert.Equal(1, monitor.Parrot.WidenPx);   // 11/20：横も左右 1 ドットずつ伸ばす
        Assert.True(monitor.TempMemory.Enabled); Assert.Equal("acpi", monitor.TempMemory.Source);
        Assert.Equal("#FF3B30", monitor.TempMemory.TempColor); Assert.Equal("#FFFFFF", monitor.TempMemory.MemoryColor);
    }

    [Theory]
    [InlineData("""{"monitor":{"digitStyle":"thin"}}""")]
    [InlineData("""{"monitor":{"parrot":{"cpuText":"bottom"},"tempMemory":{"enabled":true}}}""")]
    public void OldMonitorJsonReceivesNewDefaults(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.True(settings.Monitor.Cpu.Enabled);
        Assert.Equal("#64D2FF", settings.Monitor.Cpu.Color);
        Assert.True(settings.Monitor.TempMemory.Split);
        // 明示した従来の設定は保持し、省略時だけ新しい既定にする。
        Assert.Equal(json.Contains("bottom") ? "bottom" : "none", settings.Monitor.Parrot.CpuText);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(-1, false)]
    [InlineData(6, false)]
    public void ParrotWidenPxAcceptsZeroToFive(int widen, bool valid)
    {
        var settings = new AppSettings { Monitor = new() { Parrot = new() { WidenPx = widen } } };
        if (valid) settings.Validate();
        else Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Fact]
    public void SplitAndCpuSettingsRoundTripAndAllowLegacyLayout()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"monitor":{"cpu":{"enabled":false,"color":"#aBc123"},
              "parrot":{"cpuText":"bottom","stretch":false},"tempMemory":{"split":false}}}
            """, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.False(settings.Monitor.Parrot.Stretch);
        Assert.False(settings.Monitor.Cpu.Enabled);
        Assert.Equal("#aBc123", settings.Monitor.Cpu.Color);
        Assert.False(settings.Monitor.TempMemory.Split);
        Assert.Equal("bottom", settings.Monitor.Parrot.CpuText);
        string json = JsonSerializer.Serialize(settings, AppSettings.JsonOptions);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        restored.Validate();
        Assert.Equal(settings.Monitor, restored.Monitor);
        using var saved = JsonDocument.Parse(json);
        var monitor = saved.RootElement.GetProperty("monitor");
        Assert.False(monitor.GetProperty("cpu").GetProperty("enabled").GetBoolean());
        Assert.Equal("#aBc123", monitor.GetProperty("cpu").GetProperty("color").GetString());
        Assert.False(monitor.GetProperty("tempMemory").GetProperty("split").GetBoolean());
    }

    [Theory]
    [InlineData("""{"monitor":{"cpu":null}}""", "monitor.cpu")]
    [InlineData("""{"monitor":{"cpu":{"color":null}}}""", "monitor.cpu.color")]
    [InlineData("""{"monitor":{"cpu":{"color":"blue"}}}""", "monitor.cpu.color")]
    [InlineData("""{"monitor":{"cpu":{"color":"#64D2F"}}}""", "monitor.cpu.color")]
    [InlineData("""{"monitor":{"cpu":{"color":"64D2FF"}}}""", "monitor.cpu.color")]
    [InlineData("""{"monitor":{"cpu":{"color":"#GG0000"}}}""", "monitor.cpu.color")]
    [InlineData("""{"monitor":{"cpu":{"color":"#FF64D2FF"}}}""", "monitor.cpu.color")]
    [InlineData("""{"monitor":{"cpu":{"color":"# 12345"}}}""", "monitor.cpu.color")]
    [InlineData("""{"monitor":{"cpu":{"color":"#12345 "}}}""", "monitor.cpu.color")]
    public void InvalidCpuSettingsAreRejected(string json, string field)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        Assert.Contains(field, Assert.Throws<InvalidDataException>(settings.Validate).Message);
    }

    [Fact]
    public void OldThresholdKeysAreIgnored()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            "{\"monitor\":{\"tempMemory\":{\"tempWarn\":80,\"tempHot\":90,\"memoryWarn\":85}}}", AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal("#FF3B30", settings.Monitor.TempMemory.TempColor);
    }

    [Fact]
    public void JsonRoundTripIncludesEverySetting()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"monitor":{"enabled":false,"sampleIntervalMs":1500,
              "parrot":{"enabled":false,"cpuText":"top","rainbow":false,"cpuTextColor":"#00CCFF"},
              "tempMemory":{"enabled":false,"tempColor":"#FF0000","memoryColor":"#EEEEEE"}}}
            """, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal(new MonitorSettings
        {
            Enabled = false, SampleIntervalMs = 1500,
            Parrot = new() { Enabled = false, CpuText = "top", Rainbow = false, CpuTextColor = "#00CCFF" },
            TempMemory = new() { Enabled = false, TempColor = "#FF0000", MemoryColor = "#EEEEEE" }
        }, settings.Monitor);
        string json = JsonSerializer.Serialize(settings, AppSettings.JsonOptions);
        Assert.Equal(settings.Monitor, JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!.Monitor);
        using var saved = JsonDocument.Parse(json);
        var monitor = saved.RootElement.GetProperty("monitor");
        Assert.Equal(1500, monitor.GetProperty("sampleIntervalMs").GetInt32());
        Assert.Equal("top", monitor.GetProperty("parrot").GetProperty("cpuText").GetString());
        Assert.Equal("#EEEEEE", monitor.GetProperty("tempMemory").GetProperty("memoryColor").GetString());
    }

    [Theory]
    [InlineData("{\"monitor\":null}", "monitor")]
    [InlineData("{\"monitor\":{\"parrot\":null}}", "parrot")]
    [InlineData("{\"monitor\":{\"tempMemory\":null}}", "tempMemory")]
    [InlineData("{\"monitor\":{\"sampleIntervalMs\":99}}", "sampleIntervalMs")]
    [InlineData("{\"monitor\":{\"sampleIntervalMs\":5001}}", "sampleIntervalMs")]
    [InlineData("{\"monitor\":{\"parrot\":{\"cpuText\":\"left\"}}}", "cpuText")]
    [InlineData("{\"monitor\":{\"parrot\":{\"cpuText\":null}}}", "cpuText")]
    [InlineData("{\"monitor\":{\"parrot\":{\"cpuTextColor\":\"white\"}}}", "cpuTextColor")]
    [InlineData("{\"monitor\":{\"tempMemory\":{\"tempColor\":\"#F00\"}}}", "tempColor")]
    [InlineData("{\"monitor\":{\"digitStyle\":\"coretemp\"}}", "digitStyle")]
    [InlineData("{\"monitor\":{\"tempMemory\":{\"source\":\"ACPI\"}}}", "source")]
    [InlineData("{\"monitor\":{\"tempMemory\":{\"source\":\"wmi\"}}}", "source")]
    [InlineData("{\"monitor\":{\"tempMemory\":{\"memoryColor\":null}}}", "memoryColor")]
    public void InvalidSettingsAreRejectedWithFieldName(string json, string field)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        Assert.Contains(field, Assert.Throws<InvalidDataException>(settings.Validate).Message);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(15, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void BatteryPictureHeightRange(int height, bool ok)
    {
        Assert.Equal(15, new BatterySettings().PictureHeight);
        var settings = new AppSettings { Battery = new() { PictureHeight = height } };
        if (ok) settings.Validate();
        else Assert.Contains("pictureHeight", Assert.Throws<InvalidDataException>(settings.Validate).Message);
    }

    [Theory]
    [InlineData(100, "bottom")]
    [InlineData(5000, "top")]
    [InlineData(1000, "none")]
    public void ValidBoundariesAreAccepted(int interval, string text) =>
        new AppSettings { Monitor = new() { SampleIntervalMs = interval, Parrot = new() { CpuText = text } } }.Validate();
}
