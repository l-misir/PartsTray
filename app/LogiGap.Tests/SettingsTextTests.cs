using System.Text.Json;
using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class SettingsTextTests
{
    [Theory]
    [InlineData("LUR", "←↑→", "LUR")]
    [InlineData("←U→", "←↑→", "LUR")]
    [InlineData("D←U→", "↓←↑→", "DLUR")]
    [InlineData("↓", "↓", "D")]
    public void GestureTextAcceptsLettersArrowsAndMixtures(string text, string arrows, string pattern)
    {
        Assert.Equal(pattern, SettingsText.ParsePattern(text));
        Assert.Equal(arrows, SettingsText.PatternArrows(text));
        Assert.Equal(pattern, SettingsText.ParsePattern(arrows));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("L U")]
    [InlineData("LL")] [InlineData("L←")] [InlineData("↑U")] [InlineData("RR")]
    [InlineData("DD")] [InlineData("lur")] [InlineData("LX")] [InlineData("↖")]
    public void GestureTextRejectsInvalidDirectionsAndRepeats(string? text) =>
        Assert.Throws<InvalidDataException>(() => SettingsText.ParsePattern(text));

    [Fact]
    public void GestureLengthBoundariesAreEnforced()
    {
        string pattern = string.Concat(Enumerable.Repeat("LR", 32));
        Assert.Equal(pattern, SettingsText.ParsePattern(pattern));
        Assert.Throws<InvalidDataException>(() => SettingsText.ParsePattern(pattern + "U"));
    }

    [Fact]
    public void DuplicateShapesMarkBothRowsEvenWhenNotationDiffers()
    {
        (string, string)[] rows = [("LUR", "Back"), ("R", "Forward"), ("←↑→", "Close")];
        var errors = SettingsText.GestureErrors(rows);
        Assert.Equal(new[] { 0, 2 }, errors.Keys.Order());
        Assert.Throws<InvalidDataException>(() => SettingsText.ParseGestures(rows));
    }

    [Fact]
    public void InvalidRowsKeepTheirIndexes()
    {
        (string, string)[] rows = [("L", "Back"), ("UU", "Refresh"), ("D", "Unknown"), ("", "Close")];
        Assert.Equal(new[] { 1, 2, 3 }, SettingsText.GestureErrors(rows).Keys.Order());
    }

    [Fact]
    public void GestureListAllowsEmptyAnd256ButRejects257Rows()
    {
        Assert.Empty(SettingsText.ParseGestures([]));
        var rows = Enumerable.Range(0, 257).Select(i =>
            (string.Concat(Enumerable.Range(0, 9).Select(bit => (i & (1 << bit)) == 0 ? "LU" : "RU")), "Back")).ToArray();
        var accepted = SettingsText.ParseGestures(rows[..256]);
        Assert.Equal(256, accepted.Count);
        new AppSettings { Gesture = new() { Gestures = accepted } }.Validate();
        Assert.Equal(256, Assert.Single(SettingsText.GestureErrors(rows)).Key);
        Assert.Throws<InvalidDataException>(() => SettingsText.ParseGestures(rows));
    }

    [Theory]
    [InlineData("ToggleDpi", "ToggleDpi", null)]
    [InlineData("Ctrl+C", "Keys", "Ctrl+C")]
    [InlineData("Ctrl+V", "Keys", "Ctrl+V")]
    [InlineData("PrintScreen", "Keys", "PrintScreen")]
    [InlineData("Win+Up", "Keys", "Win+Up")]
    [InlineData("None", null, null)]
    public void EveryButtonChoiceConvertsBothWays(string choice, string? action, string? keys)
    {
        var button = SettingsText.ParseButton(choice);
        Assert.Equal(action, button?.Action); Assert.Equal(keys, button?.Keys);
        Assert.Equal(choice, SettingsText.ButtonChoice(button));
        var gap = SettingsText.SetButton(new GapSettings(), "0x0052", choice);
        Assert.Equal(choice != "None", gap.Buttons.ContainsKey("0x0052"));
        new AppSettings { GapFiller = gap }.Validate();
    }

    [Fact]
    public void ButtonEditingDoesNotMutateLoadedSettingsOrOtherButtons()
    {
        var gap = new GapSettings();
        var edited = SettingsText.SetButton(gap, "0x0056", "None");
        Assert.Equal("Ctrl+C", gap.Buttons["0x0056"].Keys);
        Assert.DoesNotContain("0x0056", edited.Buttons.Keys);
        Assert.Equal(gap.Buttons["0x0053"], edited.Buttons["0x0053"]);
        var restored = SettingsText.SetButton(edited, "0x0056", "Ctrl+C");
        Assert.Equal(gap.Buttons["0x0056"], restored.Buttons["0x0056"]);
    }

    [Fact]
    public void UnchangedTogglePreservesUnusedKeysValue()
    {
        var original = new GapSettings { Buttons = new() { ["0x0052"] = new() { Action = "ToggleDpi", Keys = "unused" } } };
        var edited = SettingsText.SetButton(original, "0x0052", "ToggleDpi");
        Assert.Equal("unused", edited.Buttons["0x0052"].Keys);
    }

    [Theory]
    [InlineData("None", "0x00C4")]
    [InlineData("unknown", "0x0052")]
    [InlineData("Ctrl+X", "0x0056")]
    public void UnsupportedButtonEditsFail(string choice, string cid) =>
        Assert.Throws<InvalidDataException>(() => SettingsText.SetButton(new GapSettings(), cid, choice));

    [Theory]
    [InlineData(0x0052)] [InlineData(0x0056)] [InlineData(0x0053)] [InlineData(0x00C3)] [InlineData(0x01A0)]
    public void OmittedButtonIsNeverDivertedOrExecuted(int cid)
    {
        var gap = SettingsText.SetButton(new GapSettings { Enabled = true }, $"0x{cid:X4}", "None");
        var h = new Harness(new AppSettings { GapFiller = gap });
        h.Activate();
        Assert.Equal(SessionState.Active, h.Session.State);
        Assert.DoesNotContain(h.Writes, r => r[2] == h.Features[0x1B04] && HidReport.U16(r.AsSpan(4)) == cid);
        int count = h.Writes.Length;
        h.Buttons((ushort)cid); h.Session.Pump();
        Assert.Empty(h.Input.KeyEvents); Assert.Equal(count, h.Writes.Length);
    }

    [Theory]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("#aB12fF", "#AB12FF")]
    [InlineData("#000000", "#000000")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void ColorsAreCanonicalAndAcceptedByAllColorSettings(string input, string expected)
    {
        string value = SettingsText.ColorText(input); Assert.Equal(expected, value);
        new AppSettings
        {
            Gesture = new() { Trail = new() { ValidColor = value, InvalidColor = value } },
            Monitor = new() { Cpu = new() { Color = value }, Parrot = new() { CpuTextColor = value },
                TempMemory = new() { TempColor = value, MemoryColor = value } }
        }.Validate();
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("red")] [InlineData("#FFF")]
    [InlineData("FFFFFF")] [InlineData("#FFFFFFFF")] [InlineData("#GGFFFF")]
    [InlineData("# 12345")] [InlineData("#12345 ")] [InlineData(" #ABCDEF")] [InlineData("#ＡＢＣＤＥＦ")]
    public void MalformedColorsAreRejected(string? text) =>
        Assert.Throws<InvalidDataException>(() => SettingsText.ColorText(text));

    [Fact]
    public void ProcessLinesIgnoreBlankLinesAndKeepNamesAndCase()
    {
        Assert.Equal(new[] { "chrome.exe", "MyApp.EXE", "app" }, SettingsText.ProcessLines(" chrome.exe\r\n\r\n MyApp.EXE \napp\n"));
        Assert.Empty(SettingsText.ProcessLines(" \r\n "));
    }

    [Theory]
    [InlineData("C:\\app.exe")] [InlineData("folder/app.exe")] [InlineData("folder\\app.exe")]
    public void ProcessPathsAreRejected(string text) => Assert.Throws<InvalidDataException>(() => SettingsText.ProcessLines(text));

    [Fact]
    public void DefaultsThroughEditorConversionsSaveAndLoadIdentically()
    {
        var original = new AppSettings();
        var gap = original.GapFiller;
        foreach (ushort cid in GapSession.ButtonOrder)
        {
            string key = $"0x{cid:X4}";
            gap = SettingsText.SetButton(gap, key, SettingsText.ButtonChoice(gap.Buttons.GetValueOrDefault(key)));
        }
        var edited = original with
        {
            GapFiller = gap,
            Gesture = original.Gesture with
            {
                Gestures = SettingsText.ParseGestures(original.Gesture.Gestures.Select(g => (SettingsText.PatternArrows(g.Key), g.Value)).ToArray()),
                BrowserProcesses = SettingsText.ProcessLines(string.Join("\r\n", original.Gesture.BrowserProcesses)),
                Trail = original.Gesture.Trail with { ValidColor = SettingsText.ColorText(original.Gesture.Trail.ValidColor) }
            }
        };
        var value = SettingsText.MergeEditable(original, edited);
        Assert.Equal(Json(original), Json(value));
        AssertRoundTrip(value);
    }

    [Fact]
    public void EditingAndDefaultResetPreserveHiddenValuesAndUnknownModules()
    {
        var original = new AppSettings
        {
            Battery = new() { PictureHeight = 19, Hidden = ["見つからない機器"] },
            Modules = new() { ["future"] = JsonSerializer.SerializeToElement(new { enabled = true, items = new[] { 1, 2 } }) }
        };
        var edited = new AppSettings
        {
            GapFiller = new() { MouseWpid = "not editable", OwnSwid = 14, Enabled = false, Dpi = new() { Primary = 8000 } },
            Gesture = new() { Enabled = false, Gestures = SettingsText.ParseGestures([("←↓", "Close")]) },
            TitleBar = new() { ShiftClick = "None" }, Wheel = new() { FastScrollMultiplier = 100 },
            Monitor = new() { SampleIntervalMs = 5000, Cpu = new() { Color = "#123456" } },
            Battery = new() { PictureHeight = 8, Hidden = ["見つからない機器", "手で追加"] },
            Diagnostics = new() { WheelTrace = true }, MoveDisabledWindows = false,
            Modules = new() { ["discard"] = JsonSerializer.SerializeToElement(0) }
        };
        var value = SettingsText.MergeEditable(original, edited);
        Assert.Equal("0xB042", value.GapFiller.MouseWpid); Assert.Equal(5, value.GapFiller.OwnSwid);
        Assert.Equal(19, value.Battery.PictureHeight); Assert.Same(original.Modules, value.Modules);
        Assert.Equal(8000, value.GapFiller.Dpi.Primary); Assert.False(value.GapFiller.Enabled);
        Assert.Equal(edited.Gesture, value.Gesture); Assert.Equal(edited.TitleBar, value.TitleBar);
        Assert.Equal(edited.Wheel, value.Wheel); Assert.Equal(edited.Monitor, value.Monitor);
        Assert.Equal(edited.Diagnostics, value.Diagnostics); Assert.False(value.MoveDisabledWindows);
        Assert.Equal(edited.Battery.Hidden, value.Battery.Hidden); AssertRoundTrip(value);
        var reset = SettingsText.MergeEditable(original, new AppSettings());
        Assert.Equal(19, reset.Battery.PictureHeight); Assert.Same(original.Modules, reset.Modules);
        Assert.Empty(reset.Battery.Hidden); Assert.False(reset.GapFiller.Enabled); // 指示書 N: 配布先では肩代わりを既定で無効にする。
        AssertRoundTrip(reset);
    }

    [Fact]
    public void MergingStillUsesExistingValidation()
    {
        var original = new AppSettings();
        Assert.Throws<InvalidDataException>(() => SettingsText.MergeEditable(original,
            original with { Battery = original.Battery with { LowHot = 20, LowWarn = 20 } }));
        Assert.Throws<InvalidDataException>(() => SettingsText.MergeEditable(original,
            original with { GapFiller = original.GapFiller with { Dpi = new() { Primary = 201 } } }));
        Assert.Throws<InvalidDataException>(() => SettingsText.MergeEditable(original,
            original with { GapFiller = original.GapFiller with { ThumbWheel = new() { SmoothMs = 7, SmoothIntervalMs = 8 } } }));
    }

    private static string Json(AppSettings value) => JsonSerializer.Serialize(value, AppSettings.JsonOptions);
    private static void AssertRoundTrip(AppSettings value)
    {
        // テスト出力先（app/ 配下）に限って保存し、実アプリの config.json を使わない。
        string path = Path.Combine(AppContext.BaseDirectory, $"settings-test-{Guid.NewGuid():N}.json");
        try { AppSettings.Save(path, value); Assert.Equal(Json(value), Json(AppSettings.Load(path))); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
