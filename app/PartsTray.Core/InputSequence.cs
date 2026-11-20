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
        _ => throw new ArgumentException("未対応のキー操作", nameof(keys))
    };
    private static KeyStroke[] Chord(ushort modifier, ushort key, bool extended)
    {
        uint flag = extended ? 1u : 0u;
        return [new(modifier, 0), new(key, flag), new(key, flag | 2), new(modifier, 2)];
    }
}
