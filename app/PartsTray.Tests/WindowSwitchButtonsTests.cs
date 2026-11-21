// 肩代わりボタンが「前へ」の設定キーを送り、空なら送らないことを偽物の機器で検証します。
// PreviousMaximized の送信や設定変更の反映を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WindowSwitchButtonsTests
{
    [Fact]
    public void PreviousButtonFollowsKeyChangesAndSkipsEmptyKeys()
    {
        var config = new AppSettings
        {
            GapFiller = new() { Enabled = true, Buttons = new() { ["0x0053"] = new() { Action = "PreviousMaximized" } } }
        };
        var h = new Harness(config);
        h.Activate();
        void Press() { h.Buttons(0x53); h.Session.Pump(); h.Buttons(); h.Session.Pump(); }
        Press();
        Assert.Empty(h.Input.KeyEvents);
        Assert.Contains(h.Log.Lines, l => l.Contains("ウィンドウ操作のキーが空"));
        h.Session.UpdateSettings(config with { WindowKeys = new() { PreviousMaximized = "Ctrl+Alt+F12" } });
        Press();
        h.Session.UpdateSettings(config with { WindowKeys = new() { PreviousMaximized = "Ctrl+Shift+F11" } });
        Press();
        h.Session.UpdateSettings(config);
        Press();
        Assert.Equal(new[] { "Ctrl+Alt+F12", "Ctrl+Shift+F11" }, h.Input.KeyEvents);
    }
}
