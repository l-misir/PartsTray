// 設定画面の文字を設定値に変換し、編集した項目を保存値へまとめます。
// 入力の検査や画面に出さない値の保持規則を変える場所です。
namespace PartsTray.Core;

// 画面からの入力だけを扱う。機器・Windows API・ファイルには触れない。
public static class SettingsText
{
    public static string ParsePattern(string? text)
    {
        string pattern = new((text ?? "").Select(c => c switch
        { '←' => 'L', '→' => 'R', '↑' => 'U', '↓' => 'D', _ => c }).ToArray());
        if (pattern.Length is < 1 or > 64 || pattern.Any(c => c is not ('L' or 'R' or 'U' or 'D')))
            throw new InvalidDataException("形は矢印または LRUD で、1〜64 文字にしてください。");
        if (pattern.Zip(pattern.Skip(1)).Any(p => p.First == p.Second))
            throw new InvalidDataException("同じ向きを続けて指定することはできません。");
        return pattern;
    }

    public static string PatternArrows(string text) => new(ParsePattern(text).Select(c => c switch
    { 'L' => '←', 'R' => '→', 'U' => '↑', _ => '↓' }).ToArray());

    public static Dictionary<int, string> GestureErrors(IReadOnlyList<(string Pattern, string Command)> rows)
    {
        var errors = new Dictionary<int, string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            try
            {
                string pattern = ParsePattern(rows[i].Pattern);
                if (seen.TryGetValue(pattern, out int first))
                    errors[first] = errors[i] = "同じ形が重複しています。";
                else seen.Add(pattern, i);
                if (rows[i].Command is not ("Back" or "Forward" or "Refresh" or "Close" or "Minimize"))
                    errors[i] = "動作を選んでください。";
            }
            catch (InvalidDataException ex) { errors[i] = ex.Message; }
            if (i >= 256) errors[i] = "ジェスチャーは 256 行までです。";
        }
        return errors;
    }

    public static Dictionary<string, string> ParseGestures(IReadOnlyList<(string Pattern, string Command)> rows)
    {
        var errors = GestureErrors(rows);
        if (errors.Count != 0) throw new InvalidDataException(errors.First().Value);
        return rows.ToDictionary(r => ParsePattern(r.Pattern), r => r.Command, StringComparer.Ordinal);
    }

    public static string ButtonChoice(ButtonSettings? value) => value switch
    {
        null => "None",
        { Action: "ToggleDpi" or "Maximize" or "NextMaximized" or "PreviousMaximized" } => value.Action,
        { Action: "Keys", Keys:"Ctrl+C" or "Ctrl+V" or "PrintScreen" or "Win+Up" } => value.Keys,
        _ => throw new InvalidDataException("未対応のボタン設定です。")
    };

    public static ButtonSettings? ParseButton(string choice) => choice switch
    {
        "None" => null,
        "ToggleDpi" or "Maximize" or "NextMaximized" or "PreviousMaximized" => new() { Action = choice },
        "Ctrl+C" or "Ctrl+V" or "PrintScreen" or "Win+Up" => new() { Keys = choice },
        _ => throw new InvalidDataException("ボタンの動作を選んでください。")
    };

    public static GapSettings SetButton(GapSettings original, string cid, string choice)
    {
        if (!GapSession.ButtonOrder.Any(c => $"0x{c:X4}" == cid))
            throw new InvalidDataException("未対応のボタンです。");
        var value = ParseButton(choice);
        var buttons = new Dictionary<string, ButtonSettings>(original.Buttons);
        if (value == null) buttons.Remove(cid);
        else if (!buttons.TryGetValue(cid, out var old) || ButtonChoice(old) != choice) buttons[cid] = value;
        return original with { Buttons = buttons };
    }

    public static string BackgroundColorText(string? text) => text == "" ? "" : ColorText(text);

    public static string ColorText(string? text)
    {
        if (text is not { Length: 7 } || text[0] != '#' || !text.Skip(1).All(Uri.IsHexDigit))
            throw new InvalidDataException("色は #RRGGBB の形（例：#00FF00）で入力してください。");
        return text.ToUpperInvariant();
    }

    public static string[] ProcessLines(string text)
    {
        // 要確認: 複数行入力の空行と行頭・行末の空白は除去する。拡張子の強制は既存 Validate に合わせて行わない。
        string[] names = text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Any(n => n.IndexOfAny(['/', '\\', ':']) >= 0))
            throw new InvalidDataException("アプリはフォルダを含めず、chrome.exe のような名前を 1 行に 1 つ入力してください。");
        return names;
    }

    public static AppSettings MergeEditable(AppSettings original, AppSettings edited, bool editGap = true)
    {
        // 要確認: 「すべて既定に戻す」も画面にある項目のみ。非表示の値と未知の Modules は保持する。
        var value = original with
        {
            RunAsAdministrator = edited.RunAsAdministrator,
            GapFiller = editGap ? edited.GapFiller with { MouseWpid = original.GapFiller.MouseWpid, OwnSwid = original.GapFiller.OwnSwid } : original.GapFiller,
            Gesture = edited.Gesture, TitleBar = edited.TitleBar, Wheel = edited.Wheel, WindowKeys = edited.WindowKeys,
            Monitor = edited.Monitor, Diagnostics = edited.Diagnostics, MoveDisabledWindows = edited.MoveDisabledWindows,
            Battery = edited.Battery with { PictureHeight = original.Battery.PictureHeight }
        };
        value.Validate();
        return value;
    }
}
