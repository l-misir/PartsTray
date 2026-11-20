using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WheelSettingsTests
{
    [Fact]
    public void DefaultsAndJsonRoundTrip()
    {
        var defaults = JsonSerializer.Deserialize<AppSettings>("{}", AppSettings.JsonOptions)!;
        Assert.Equal(40, defaults.GapFiller.ThumbWheel.SmoothMs);
        Assert.Equal(8, defaults.GapFiller.ThumbWheel.SmoothIntervalMs);
        Assert.False(defaults.Diagnostics.WheelTrace);
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"gapFiller":{"thumbWheel":{"smoothMs":60,"smoothIntervalMs":12}},"diagnostics":{"wheelTrace":true}}
            """, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal(60, settings.GapFiller.ThumbWheel.SmoothMs);
        Assert.Equal(12, settings.GapFiller.ThumbWheel.SmoothIntervalMs);
        Assert.True(settings.Diagnostics.WheelTrace);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(settings, AppSettings.JsonOptions));
        var thumb = saved.RootElement.GetProperty("gapFiller").GetProperty("thumbWheel");
        Assert.Equal(60, thumb.GetProperty("smoothMs").GetInt32());
        Assert.Equal(12, thumb.GetProperty("smoothIntervalMs").GetInt32());
        Assert.True(saved.RootElement.GetProperty("diagnostics").GetProperty("wheelTrace").GetBoolean());
    }

    [Theory]
    [InlineData(-1, 8)]
    [InlineData(201, 8)]
    [InlineData(40, 3)]
    [InlineData(100, 51)]
    [InlineData(4, 8)]
    public void InvalidSmoothingIsRejected(int smoothMs, int intervalMs)
    {
        var settings = new AppSettings { GapFiller = new() { ThumbWheel = new() { SmoothMs = smoothMs, SmoothIntervalMs = intervalMs } } };
        var error = Assert.Throws<InvalidDataException>(settings.Validate);
        Assert.Equal("thumbWheel.smoothMs は 0〜200、smoothIntervalMs は 4〜50 で、smoothMs が 0 でなければ smoothIntervalMs 以上です", error.Message);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(4, 4)]
    [InlineData(200, 50)]
    public void ValidSmoothingIsAccepted(int smoothMs, int intervalMs) =>
        new AppSettings { GapFiller = new() { ThumbWheel = new() { SmoothMs = smoothMs, SmoothIntervalMs = intervalMs } } }.Validate();

    [Fact]
    public void DiagnosticsRequired()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"diagnostics\":{\"wheelTrace\":true}}", AppSettings.JsonOptions)!;
        settings.Validate(); Assert.True(settings.Diagnostics.WheelTrace);
        Assert.Equal("diagnostics が必要です", Assert.Throws<InvalidDataException>(() => (settings with { Diagnostics = null! }).Validate()).Message);
    }
}
