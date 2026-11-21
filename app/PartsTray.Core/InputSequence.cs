namespace PartsTray.Core;

public readonly record struct KeyStroke(ushort VirtualKey, uint Flags)
{
    public const nuint ExtraInfo = 0x4C474150;
}
public static class InputSequence
{
    public static KeyStroke[] ForKeys(string keys) => keys switch
    {
        "Ctrl+C" => Chord(0x11, 0x43, false),
        "Ctrl+V" => Chord(0x11, 0x56, false),
        "Win+Up" => Chord(0x5B, 0x26, true),
        "PrintScreen" => [new(0x2C, 0), new(0x2C, 2)],
        // ウィンドウ操作のキー（「Ctrl+Alt+Shift+F10」など。HotkeyText の書き方）。肩代わり中のボタンから送るときに使う。
        _ when HotkeyText.TryParse(keys, out uint modifiers, out uint key) && key != 0 => Combination(modifiers, (ushort)key),
        _ => throw new ArgumentException("未対応のキー操作", nameof(keys))
    };
    // 修飾キーを Ctrl・Alt・Shift・Win の順に押し、キーを押して離し、修飾キーを逆の順に離す。矢印や PageUp などは拡張キー。
    private static KeyStroke[] Combination(uint modifiers, ushort key)
    {
        var held = new List<ushort>();
        if ((modifiers & HotkeyText.Control) != 0) held.Add(0x11);
        if ((modifiers & HotkeyText.Alt) != 0) held.Add(0x12);
        if ((modifiers & HotkeyText.Shift) != 0) held.Add(0x10);
        if ((modifiers & HotkeyText.Win) != 0) held.Add(0x5B);
        uint flag = key is >= 0x21 and <= 0x28 or 0x2D or 0x2E ? 1u : 0u;
        return [.. held.Select(m => new KeyStroke(m, 0)), new(key, flag), new(key, flag | 2), .. Enumerable.Reverse(held).Select(m => new KeyStroke(m, 2))];
    }
    private static KeyStroke[] Chord(ushort modifier, ushort key, bool extended)
    {
        uint flag = extended ? 1u : 0u;
        return [new(modifier, 0), new(key, flag), new(key, flag | 2), new(modifier, 2)];
    }
}
