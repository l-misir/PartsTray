// 任意キーと Hold の設定・保存往復・旧設定の互換性を検証します。
// 肩代わりの入力検査や設定画面の変換を変えるときに開きます。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class GapButtonSettingsTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+F12")]
    [InlineData("Win+Left")]
    [InlineData("Control+Alt+F24")]
    [InlineData("Shift+Space")]
    [InlineData("ctrl + alt + 9")]
    [InlineData("PrintScreen")]
    public void ArbitraryKeysValidateAndSaveLoad(string keys)
    {
        var gap = SettingsText.SetButton(new(), "0x00C4", "Keys", keys);
        var settings = RoundTrip(new() { GapFiller = gap });
        var button = settings.GapFiller.Buttons["0x00C4"];
        Assert.Equal("Keys", button.Action);
        Assert.Equal(keys, button.Keys);
        Assert.Equal(keys == "PrintScreen" ? "PrintScreen" : "Keys", SettingsText.ButtonChoice(button));
        Assert.NotEmpty(InputSequence.ForKeys(button.Keys!));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")]
    [InlineData("Ctrl")] [InlineData("F12")] [InlineData("Ctrl+Unknown")]
    [InlineData("Ctrl+Alt+F25")] [InlineData("Ctrl+A+B")] [InlineData("Ctrl++F12")]
    public void EmptyOrInvalidKeysAreRejected(string? keys)
    {
        Assert.Throws<InvalidDataException>(() => SettingsText.ParseButton("Keys", keys));
        Assert.Throws<InvalidDataException>(() => new AppSettings
        {
            GapFiller = new() { Buttons = new() { ["0x00C4"] = new() { Keys = keys } } }
        }.Validate());
    }

    [Fact]
    public void HoldSavesLoadsAndCanBeRemovedWithoutChangingDefaults()
    {
        var original = new GapSettings();
        var gap = SettingsText.SetButton(original, "0x0052", "Hold");
        var loaded = RoundTrip(new() { GapFiller = gap, WindowKeys = new() { HoldButton = "0x0052" } });
        Assert.Equal("Hold", SettingsText.ButtonChoice(loaded.GapFiller.Buttons["0x0052"]));
        Assert.Null(loaded.GapFiller.Buttons["0x0052"].Keys);
        Assert.Equal("0x0052", loaded.WindowKeys.HoldButton);
        Assert.Equal("ToggleDpi", original.Buttons["0x0052"].Action);
        Assert.False(SettingsText.SetButton(loaded.GapFiller, "0x0052", "None").Buttons.ContainsKey("0x0052"));
    }

    [Fact]
    public void EditingCustomKeyUpdatesExistingCustomKeyAndPreservesOthers()
    {
        var original = SettingsText.SetButton(new(), "0x00C4", "Keys", "Ctrl+Alt+F11");
        var edited = SettingsText.SetButton(original, "0x00C4", "Keys", "Ctrl+Alt+Shift+F12");
        Assert.Equal("Ctrl+Alt+F11", original.Buttons["0x00C4"].Keys);
        Assert.Equal("Ctrl+Alt+Shift+F12", edited.Buttons["0x00C4"].Keys);
        Assert.Same(original.Buttons["0x0052"], edited.Buttons["0x0052"]);
        var hold = SettingsText.SetButton(edited, "0x00C4", "Hold");
        Assert.Null(hold.Buttons["0x00C4"].Keys);
        Assert.False(SettingsText.SetButton(hold, "0x00C4", "None").Buttons.ContainsKey("0x00C4"));
    }

    [Fact]
    public void LegacyConfigKeepsFiveBindingsAndDoesNotAddRearButton()
    {
        const string json = """
            { "gapFiller": { "enabled": true, "buttons": {
              "0x0052": { "action": "ToggleDpi" }, "0x0056": { "keys": "Ctrl+C" },
              "0x0053": { "keys": "Ctrl+V" }, "0x00C3": { "keys": "PrintScreen" },
              "0x01A0": { "keys": "Win+Up" } } } }
            """;
        var legacy = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        var loaded = RoundTrip(legacy);
        Assert.Equal(5, loaded.GapFiller.Buttons.Count);
        Assert.DoesNotContain("0x00C4", loaded.GapFiller.Buttons.Keys);
        foreach (var (cid, button) in legacy.GapFiller.Buttons)
            Assert.Equal(button, loaded.GapFiller.Buttons[cid]);
    }

    [Theory]
    [InlineData("0xFFFF", "Hold")]
    [InlineData("0x00C4", "Unknown")]
    public void UnknownCidOrActionStillFails(string cid, string action) =>
        Assert.Throws<InvalidDataException>(() => new AppSettings
        {
            GapFiller = new() { Buttons = new() { [cid] = new() { Action = action } } }
        }.Validate());

    private static AppSettings RoundTrip(AppSettings value)
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"gap-settings-{Guid.NewGuid():N}.json");
        try { AppSettings.Save(path, value); return AppSettings.Load(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
