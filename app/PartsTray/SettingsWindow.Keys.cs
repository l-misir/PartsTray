// 設定画面に、ウィンドウ操作のキー（本当の最大化・最大化しているウィンドウの切り替え）の欄を作ります。
// 欄の説明やキーの読み取り方を変えるときはここを編集します。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private void BuildWindowKeys(TableLayoutPanel page)
    {
        ChoiceBox(page, "押しながらホイールで切り替えるボタン",
            "Options+ で、このボタンに何かを割り当てておく必要があります（割り当てがないと、押したことが PartsTray に届きません）。押したときに何も起きないよう、どのアプリも使わないキー（例：Ctrl+Alt+Shift+F12）を割り当ててください。肩代わり中も使うには、「マウスの肩代わり」で同じボタンを「押しながらホイールで切り替え用（何もしない）」にしてください。押している間だけ選び、離すか 30 秒で終了します。",
            WindowHoldButton.Choices.ToArray(), s => WindowHoldButton.Canonical(s.WindowKeys.HoldButton),
            (s, v) => s with { WindowKeys = s.WindowKeys with { HoldButton = v } });
        HotkeyField(page, "本当の最大化のキー", "前面のウィンドウを最大化します（最大化していれば元に戻す）。Windows＋↑ と違い、画面の上半分などに寄っていても最大化します。欄を選んでキーを押すと入ります。空にすると使いません。",
            s => s.WindowKeys.Maximize, (s, v) => s with { WindowKeys = s.WindowKeys with { Maximize = v } });
        HotkeyField(page, "最大化しているアプリを切り替えるキー", "同じモニターで最大化しているウィンドウを、重なっている順（最小化していったときに出てくる順）に切り替えます。押すたびに順に回ります。",
            s => s.WindowKeys.NextMaximized, (s, v) => s with { WindowKeys = s.WindowKeys with { NextMaximized = v } });
        HotkeyField(page, "最大化しているアプリを前へ戻すキー", "同じモニターの一番下にある最大化ウィンドウを前面にします。空なら使いません。",
            s => s.WindowKeys.PreviousMaximized, (s, v) => s with { WindowKeys = s.WindowKeys with { PreviousMaximized = v } });
        Number(page, "ホイールで選ぶ時間（ミリ秒）", "切り替えのキーを押してから、この時間のあいだはホイールで前後を選べます。回している間は延びます。0 で使いません。", 0, 5000,
            s => s.WindowKeys.WheelSwitchMs, (s, v) => s with { WindowKeys = s.WindowKeys with { WheelSwitchMs = (int)v } });
        Check(page, "機器のサムホイール通知でも切り替える", "Options+ が動いている間のサムホイール回転を使います。使いづらければオフにできます。切り替えと同時に、カーソルの下の窓が横にスクロールする場合があります。",
            s => s.WindowKeys.ThumbWheelFromDevice, (s, v) => s with { WindowKeys = s.WindowKeys with { ThumbWheelFromDevice = v } });
        Check(page, "切り替えの横ホイールの向きを逆にする", "サムホイールで進む向きが合わないときに切り替えます。",
            s => s.WindowKeys.ReverseHorizontal, (s, v) => s with { WindowKeys = s.WindowKeys with { ReverseHorizontal = v } });
    }

    // キーの欄。欄を選んで Ctrl か Alt を含む組み合わせを押すと「Ctrl+Alt+F10」のように入る。手で書いてもよい。Delete・BackSpace だけで空にする。
    private TextBox HotkeyField(TableLayoutPanel page, string title, string hint, Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set,
        Func<bool>? enabled = null, bool buttonKeys = false)
    {
        var box = new TextBox { Width = LogicalToDeviceUnits(240) };
        box.KeyDown += (_, e) =>
        {
            if (e.Modifiers == Keys.None && e.KeyCode is Keys.Delete or Keys.Back) { box.Text = ""; e.SuppressKeyPress = true; return; }
            if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0 || KeyName(e.KeyCode) is not { } key) return;
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            parts.Add(key);
            box.Text = string.Join("+", parts); e.SuppressKeyPress = true;
        };
        Field(page, title, box, hint);
        Bind(box, get, v => box.Text = v, () =>
        {
            if (enabled?.Invoke() == false) return "";
            string text = box.Text.Trim();
            if (buttonKeys ? !SettingsText.ValidButtonKeys(text) : !HotkeyText.TryParse(text, out _, out _)) throw new InvalidDataException($"「{title}」の書き方が違います（例：Ctrl+Alt+Shift+F10）");
            return text;
        }, (s, v) => enabled?.Invoke() == false ? s : set(s, v), () => box.Text);
        return box;
    }

    private static string? KeyName(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => key.ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.F1 and <= Keys.F24 => "F" + (key - Keys.F1 + 1),
        Keys.Up => "Up", Keys.Down => "Down", Keys.Left => "Left", Keys.Right => "Right",
        Keys.PageUp => "PageUp", Keys.PageDown => "PageDown", Keys.Home => "Home", Keys.End => "End",
        Keys.Insert => "Insert", Keys.Delete => "Delete", Keys.Space => "Space", Keys.Pause => "Pause",
        _ => null
    };
}
