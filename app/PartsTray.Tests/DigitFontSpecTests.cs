// 従来の書体名と、太さ・描き方・縦横比を足した数字フォント指定の解釈を検証します。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DigitFontSpecTests
{
    [Theory]
    [InlineData(null, "Tahoma", null, null)]
    [InlineData("Tahoma", "Tahoma", null, null)]
    [InlineData("Bahnschrift SemiBold", "Bahnschrift SemiBold", null, null)]
    [InlineData("Terminus", "Terminus", null, null)]
    [InlineData("TerminusBold", "TerminusBold", null, null)]
    [InlineData("Tahoma, Regular, Pixel", "Tahoma", DigitWeight.Regular, DigitRendering.Pixel)]
    [InlineData(" Tahoma , bOlD, sMoOtH ", "Tahoma", DigitWeight.Bold, DigitRendering.Smooth)]
    [InlineData("Terminus, Bold", "Terminus", DigitWeight.Bold, null)]
    [InlineData("Tahoma, , Pixel", "Tahoma", null, DigitRendering.Pixel)]
    [InlineData("Tahoma, Pixel", "Tahoma", null, DigitRendering.Pixel)]
    [InlineData("Tahoma, Regular, Pixel, ", "Tahoma", DigitWeight.Regular, DigitRendering.Pixel)]
    public void ParsesNameAndOptionalFields(string? value, string family, DigitWeight? weight, DigitRendering? rendering) =>
        Assert.Equal(new DigitFontSpec(family, weight, rendering), DigitFontSpec.Parse(value));

    [Theory]
    [InlineData("Tahoma, Regular, Pixel, Square2", DigitWeight.Regular, DigitRendering.Pixel)]
    [InlineData(" Tahoma , bOlD , pixel , sQuArE2 ", DigitWeight.Bold, DigitRendering.Pixel)]
    [InlineData("Tahoma, , , Square2", null, null)]
    public void ReadsSquare2InTheFourthField(string value, DigitWeight? weight, DigitRendering? rendering) =>
        Assert.Equal(new DigitFontSpec("Tahoma", weight, rendering, Square2: true), DigitFontSpec.Parse(value));

    [Fact]
    public void LegacyCenterParsesAsTheDefaultSevenSegmentFont() =>
        Assert.Equal(DigitFontSpec.Parse("Dots:SevenSeg"), DigitFontSpec.Parse("Dots:SevenSeg, Center"));

    [Theory]
    [InlineData("Dots:SevenSeg, Center", "Dots:SevenSeg, Narrow", DotDigitKind.SevenSeg, DotDigitKind.SevenSegNarrow)]
    [InlineData("Dots:SevenSeg, Narrow", " dots:sevenseg , cEnTeR ", DotDigitKind.SevenSegNarrow, DotDigitKind.SevenSeg)]
    [InlineData("Dots:SevenSeg", " dots:sevenseg , nArRoW ", DotDigitKind.SevenSeg, DotDigitKind.SevenSegNarrow)]
    public void SevenSegmentLayoutCanBeInheritedOrOverriddenPerIcon(string shared, string cpu,
        DotDigitKind inheritedKind, DotDigitKind cpuKind)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>($$$"""
            {"monitor":{"digitFont":"{{{shared}}}","cpuDigitFont":"{{{cpu}}}"}}
            """, AppSettings.JsonOptions)!;
        settings.Validate();
        var monitor = settings.Monitor;
        Assert.Equal(cpuKind, DigitFontSpec.DotSet(monitor.ResolveDigitFont(monitor.CpuDigitFont)));
        Assert.Equal(inheritedKind, DigitFontSpec.DotSet(monitor.ResolveDigitFont(monitor.TemperatureDigitFont)));
        Assert.Equal(inheritedKind, DigitFontSpec.DotSet(monitor.ResolveDigitFont(monitor.MemoryDigitFont)));
    }

    [Theory]
    [InlineData("digitFont", "Tahoma, Heavy")]
    [InlineData("cpuDigitFont", "Tahoma, Regular, ClearType")]
    [InlineData("temperatureDigitFont", ", Bold, Smooth")]
    [InlineData("memoryDigitFont", "Tahoma, Bold, Smooth, Extra")]
    [InlineData("digitFont", "Tahoma, Regular, Pixel, Square2, Extra")]
    [InlineData("cpuDigitFont", "Tahoma, Regular, Square2")]
    [InlineData("digitFont", "Dots:")]
    [InlineData("temperatureDigitFont", "Dots:CoreTamp")]
    [InlineData("memoryDigitFont", "Dots:File, Bold")]
    [InlineData("digitFont", "Dots:CoreTemp, Center")]
    [InlineData("cpuDigitFont", "Dots:SevenSeg, Centre")]
    [InlineData("memoryDigitFont", "Dots:SevenSeg, Center, Bold")]
    [InlineData("temperatureDigitFont", "Dots:SevenSeg, Narrow, Center")]
    [InlineData("cpuDigitFont", "Dots:CoreTemp, Narrow")]
    public void InvalidSpecificationNamesTheConfigField(string field, string invalid)
    {
        var json = $$$"""{"monitor":{"{{{field}}}":"{{{invalid}}}"}}""";
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        Assert.Contains($"monitor.{field}", Assert.Throws<InvalidDataException>(settings.Validate).Message);
    }
}
