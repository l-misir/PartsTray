// 押しながらホイールで選ぶボタンの設定と、押下・解放・30 秒の期限を管理します。
// 選択肢や安全終了の条件を変えるときに開きます。OS には触れません。
using System.Globalization;

namespace PartsTray.Core;

public static class WindowHoldButton
{
    public static IReadOnlyList<(string Value, string Label)> Choices { get; } = Array.AsReadOnly<(string, string)>([
        // 11/21 ユーザー：「設定したいのは縦スクロールボタン（ホイールクリック）」。ホイールを押し込んだまま回す使い方。
        ("", "使わない"), ("0x0052", "ホイールクリック（0x0052）"), ("0x00C4", "ホイールの後ろのボタン（0x00C4）"),
        ("0x00C3", "ジェスチャーボタン（0x00C3）"), ("0x01A0", "親指パネル（0x01A0）"),
        ("0x0056", "進む（0x0056）"), ("0x0053", "戻る（0x0053）")]);

    public static bool TryParse(string? text, out ushort cid)
    {
        cid = 0;
        if (text == "") return true;
        return text is { Length: 6 } && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out cid) &&
            cid is 0x52 or 0xC4 or 0xC3 or 0x1A0 or 0x56 or 0x53;
    }

    public static string Canonical(string text) => TryParse(text, out ushort cid) && cid != 0 ? $"0x{cid:X4}" : "";
    public static string Label(string text) => Choices.First(c => c.Value == Canonical(text)).Label;
}

public enum HoldButtonChange { None, Started, Ended }

public sealed class WindowHoldSelection(WindowSwitchWheel wheel, Func<TimeSpan> now)
{
    private WindowKeySettings settings = new();
    private ushort cid;
    private bool pressed;
    private TimeSpan deadline;
    public bool IsActive { get; private set; }
    public bool IsPressed => pressed;

    public void Configure(WindowKeySettings value)
    {
        value.Validate();
        Stop();
        settings = value;
        WindowHoldButton.TryParse(value.HoldButton, out cid);
        pressed = false;
    }

    public HoldButtonChange ReceiveButtons(IReadOnlyList<ushort> buttons)
    {
        bool expired = Expire();
        bool down = cid != 0 && buttons.Contains(cid);
        if (pressed == down) return expired ? HoldButtonChange.Ended : HoldButtonChange.None;
        pressed = down;
        if (!down) return Stop() || expired ? HoldButtonChange.Ended : HoldButtonChange.None;
        wheel.Start(settings, untilRelease: true);
        deadline = now() + TimeSpan.FromSeconds(30);
        IsActive = true;
        return HoldButtonChange.Started;
    }

    // タイムアウト・キー操作などで中断しても、解放を受け取るまで再始動しない。
    public bool Stop()
    {
        if (!IsActive) return false;
        IsActive = false;
        wheel.Stop();
        return true;
    }

    public bool Expire() => IsActive && now() >= deadline && Stop();
}
