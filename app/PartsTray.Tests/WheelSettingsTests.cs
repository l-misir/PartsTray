// ホイール設定の既定値、JSON の読み書き、値の検証を確認します。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WheelSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"wheel":{"fastScrollOnScrollBar":true}}""")]
    public void MissingFastScrollExclusionsDefaultToAcrobatAndReader(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal(new[] { "Acrobat.exe", "AcroRd32.exe" }, settings.Wheel.FastScrollExcludedProcesses);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""["Code.exe","Other.exe"]""")]
    public void FastScrollExclusionsReplaceDefaultsAndRoundTrip(string names)
    {
        var settings = LoadFastScrollExclusions(names);
        settings.Validate();
        var expected = JsonSerializer.Deserialize<string[]>(names)!;
        Assert.Equal(expected, settings.Wheel.FastScrollExcludedProcesses);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(settings, AppSettings.JsonOptions));
        Assert.Equal(expected, saved.RootElement.GetProperty("wheel").GetProperty("fastScrollExcludedProcesses").Deserialize<string[]>());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""[""]""")]
    [InlineData("""["folder/app.exe"]""")]
    public void InvalidFastScrollExclusionsAreRejected(string names)
    {
        var settings = LoadFastScrollExclusions(names);
        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    private static AppSettings LoadFastScrollExclusions(string names) =>
        JsonSerializer.Deserialize<AppSettings>("{\"wheel\":{\"fastScrollExcludedProcesses\":" + names + "}}", AppSettings.JsonOptions)!;

    [Theory]
    [InlineData("{}", true, false)]
    [InlineData("""{"wheel":{"fastScrollOnScrollBar":true}}""", true, false)]
    [InlineData("""{"wheel":{"edgeAssist":false}}""", false, false)]
    [InlineData("""{"wheel":{"edgeRedirect":true}}""", true, true)]
    [InlineData("""{"wheel":{"edgeAssist":false,"edgeRedirect":true}}""", false, true)]
    public void EdgeOptionsLoadAndSaveIndependently(string json, bool edgeAssist, bool edgeRedirect)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal(edgeAssist, settings.Wheel.EdgeAssist);
        Assert.Equal(edgeRedirect, settings.Wheel.EdgeRedirect);

        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(settings, AppSettings.JsonOptions));
        var wheel = saved.RootElement.GetProperty("wheel");
        Assert.Equal(edgeAssist, wheel.GetProperty("edgeAssist").GetBoolean());
        Assert.Equal(edgeRedirect, wheel.GetProperty("edgeRedirect").GetBoolean());
    }

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

    [Theory]
    [InlineData("{}", false, false)]
    [InlineData("""{"diagnostics":{"wheelTrace":true}}""", false, true)]
    [InlineData("""{"diagnostics":{"scrollProbe":true}}""", true, false)]
    public void ScrollProbeSettingSurvivesWheelEdits(string json, bool probe, bool trace)
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        Assert.Equal(probe, loaded.Diagnostics.ScrollProbe);
        var edited = loaded with { Wheel = loaded.Wheel with { FastScrollOnScrollBar = true } };
        var merged = SettingsText.MergeEditable(loaded, edited);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(merged, AppSettings.JsonOptions));
        var diagnostics = saved.RootElement.GetProperty("diagnostics");
        Assert.Equal(probe, diagnostics.GetProperty("scrollProbe").GetBoolean());
        Assert.Equal(trace, diagnostics.GetProperty("wheelTrace").GetBoolean());
    }
}
