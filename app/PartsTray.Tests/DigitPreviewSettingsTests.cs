// 数字の試し表示の設定を読み書きし、使える数と設定画面からの保存を確かめます。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DigitPreviewSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"diagnostics":{"wheelTrace":true}}""")]
    [InlineData("""{"diagnostics":{"digitPreview":[]}}""")]
    public void MissingOrEmptyListDisablesPreview(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Empty(settings.Diagnostics.DigitPreview);
    }

    [Fact]
    public void ListRetainsOrderAndDuplicatesOnDiskAndInSettingsWindow()
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"digit-preview-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"diagnostics":{"digitPreview":[999,0,100,100,37]}}""");
            var loaded = AppSettings.Load(path);
            int[] expected = [999, 0, 100, 100, 37];
            Assert.Equal(expected, loaded.Diagnostics.DigitPreview);

            var edited = new AppSettings { Diagnostics = new() { WheelTrace = true } };
            AppSettings.Save(path, SettingsText.MergeEditable(loaded, edited));
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var diagnostics = json.RootElement.GetProperty("diagnostics");
            Assert.True(diagnostics.GetProperty("wheelTrace").GetBoolean());
            Assert.Equal(expected, diagnostics.GetProperty("digitPreview").EnumerateArray().Select(element => element.GetInt32()));
            Assert.Equal(expected, AppSettings.Load(path).Diagnostics.DigitPreview);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("[0,-1]")]
    [InlineData("[1000,999]")]
    [InlineData("null")]
    public void NullOrOutOfRangeListFailsValidation(string list)
    {
        string json = $$$"""{"diagnostics":{"digitPreview":{{{list}}}}}""";
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        Assert.Contains("diagnostics.digitPreview", Assert.Throws<InvalidDataException>(settings.Validate).Message);
    }

    [Theory]
    [InlineData("[8,1.5]")]
    [InlineData("""["100"]""")]
    [InlineData("[null]")]
    [InlineData("100")]
    public void JsonRequiresAnArrayOfIntegers(string list)
    {
        string json = $$$"""{"diagnostics":{"digitPreview":{{{list}}}}}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions));
    }
}
