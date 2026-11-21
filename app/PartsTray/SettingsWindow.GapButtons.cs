// 肩代わりする 6 ボタンの動作と、任意のキー入力欄を作ります。
// ボタン名・動作の選択肢・キー欄の有効条件を変えるときに開きます。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private void BuildGapButtons(TableLayoutPanel page)
    {
        string[] names = ["ホイールクリック（中ボタン）", "ホイールの後ろのボタン", "進む（親指の前側）",
            "戻る（親指の後側）", "ジェスチャーボタン（親指の上）", "親指パネル（ハプティック）"];
        for (int i = 0; i < GapSession.ButtonOrder.Count; i++)
        {
            string cid = $"0x{GapSession.ButtonOrder[i]:X4}";
            var choice = ChoiceBox(page, names[i],
                "肩代わり中の動作です。変更は次の接続から有効です。本当の最大化・切り替えは「ウィンドウ・ホイール」のキーを送ります。",
                [("ToggleDpi", "ポインタの速さを切り替える"), ("Ctrl+C", "Ctrl+C（コピー）"), ("Ctrl+V", "Ctrl+V（貼り付け）"),
                 ("PrintScreen", "PrintScreen"), ("Win+Up", "Win+↑（最大化）"), ("Maximize", "本当の最大化"),
                 ("NextMaximized", "最大化しているアプリの切り替え"), ("PreviousMaximized", "最大化しているアプリを前へ"),
                 ("Keys", "キーを指定…"), ("Hold", "押しながらホイールで切り替え用（何もしない）"), ("None", "肩代わりしない")],
                s => SettingsText.ButtonChoice(s.GapFiller.Buttons.GetValueOrDefault(cid)),
                (s, v) => v == "Keys" ? s : s with { GapFiller = SettingsText.SetButton(s.GapFiller, cid, v) });
            bool CustomKeys() => (choice.SelectedItem as Choice)?.Value == "Keys";
            var keys = HotkeyField(page, names[i] + "のキー",
                "「キーを指定…」を選んで入力します（例：Ctrl+Alt+Shift+F12）。Ctrl または Alt を含むキーを押すか、文字で入力してください。空欄では保存できません。",
                s => s.GapFiller.Buttons.GetValueOrDefault(cid) is { Action: "Keys" } button ? button.Keys ?? "" : "",
                (s, v) => s with { GapFiller = SettingsText.SetButton(s.GapFiller, cid, "Keys", v) }, CustomKeys, buttonKeys: true);
            void EnableKeys() { keys.Enabled = CustomKeys(); keys.Parent!.Visible = CustomKeys(); }
            choice.SelectedIndexChanged += (_, _) => EnableKeys();
            loaders.Add(_ => EnableKeys());
        }
    }
}
