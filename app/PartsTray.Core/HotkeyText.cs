// 「Ctrl+Alt+Shift+F10」のようなキーの組み合わせの文字を、Windows のホットキー（修飾キーと仮想キー）に直します。
// 使えるキーの名前を増やすときはここを編集します。OS にはアクセスしない純粋な関数です。
namespace PartsTray.Core;

public static class HotkeyText
{
    // RegisterHotKey の修飾キー。MOD_NOREPEAT は押しっぱなしで何度も動かないようにする。
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8, NoRepeat = 0x4000;

    // 空なら「使わない」（true を返し、修飾キー・キーとも 0）。読めない文字なら false。修飾キーだけ・キーだけは不可。
    public static bool TryParse(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0; key = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= Control; continue;
                case "ALT": modifiers |= Alt; continue;
                case "SHIFT": modifiers |= Shift; continue;
                case "WIN": case "WINDOWS": modifiers |= Win; continue;
            }
            if (key != 0 || Key(part) is not { } vk) return false;
            key = vk;
        }
        if (key == 0 || modifiers == 0) { modifiers = 0; key = 0; return false; }
        return true;
    }

    // キーの名前 → 仮想キー。A〜Z、0〜9、F1〜F24、矢印、PageUp など。
    private static uint? Key(string name)
    {
        string n = name.ToUpperInvariant();
        if (n.Length == 1 && n[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') return n[0];
        if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n[1..], out int f) && f is >= 1 and <= 24) return (uint)(0x70 + f - 1);
        return n switch
        {
            "UP" or "↑" => 0x26, "DOWN" or "↓" => 0x28, "LEFT" or "←" => 0x25, "RIGHT" or "→" => 0x27,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22, "HOME" => 0x24, "END" => 0x23, "INSERT" => 0x2D, "DELETE" => 0x2E,
            "SPACE" => 0x20, "TAB" => 0x09, "PAUSE" => 0x13,
            _ => null
        };
    }
}
