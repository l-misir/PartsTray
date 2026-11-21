// 機器の種類を設定・HID・Bluetooth・名前の順で判断します。
// 判定の優先順位や名前の手掛かりを変える場所です。OS にはアクセスしません。
namespace PartsTray.Core;

public static class BatteryKinds
{
    public static bool TryParse(string? value, out BatteryDeviceKind kind)
    {
        kind = value switch
        {
            "mouse" => BatteryDeviceKind.Mouse, "keyboard" => BatteryDeviceKind.Keyboard,
            "headphones" => BatteryDeviceKind.Headphones, "earbuds" => BatteryDeviceKind.Earbuds,
            "gamepad" => BatteryDeviceKind.Gamepad, "phone" => BatteryDeviceKind.Phone, "tablet" => BatteryDeviceKind.Tablet,
            _ => BatteryDeviceKind.Other
        };
        return value is "mouse" or "keyboard" or "headphones" or "earbuds" or "gamepad" or "phone" or "tablet" or "other";
    }

    public static string Label(BatteryDeviceKind kind) => kind switch
    {
        BatteryDeviceKind.Mouse => "マウス", BatteryDeviceKind.Keyboard => "キーボード",
        BatteryDeviceKind.Headphones => "ヘッドホン", BatteryDeviceKind.Earbuds => "イヤホン",
        BatteryDeviceKind.Gamepad => "ゲームパッド", BatteryDeviceKind.Phone => "スマホ", BatteryDeviceKind.Tablet => "タブレット", _ => "その他"
    };

    public static BatteryDeviceKind? FromHid(IEnumerable<string> compatibleIds)
    {
        var ids = compatibleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 要確認: 複数種類の HID を持つ入れ物は、指定の記載順（マウス→キーボード→ゲーム）で決める。
        if (ids.Contains("HID_DEVICE_SYSTEM_MOUSE")) return BatteryDeviceKind.Mouse;
        if (ids.Contains("HID_DEVICE_SYSTEM_KEYBOARD")) return BatteryDeviceKind.Keyboard;
        if (ids.Contains("HID_DEVICE_SYSTEM_GAME")) return BatteryDeviceKind.Gamepad;
        return null;
    }

    public static BatteryDeviceKind? FromClassOfDevice(uint value)
    {
        uint major = (value >> 8) & 31;
        if (major == 4) return BatteryDeviceKind.Headphones;
        if (major != 5) return null;
        uint peripheral = (value >> 6) & 3;
        if (peripheral is 1 or 3) return BatteryDeviceKind.Keyboard;
        if (peripheral == 2) return BatteryDeviceKind.Mouse;
        return ((value >> 2) & 15) is 1 or 2 ? BatteryDeviceKind.Gamepad : null;
    }

    public static BatteryDeviceKind? FromName(string name)
    {
        bool Has(params string[] parts) => parts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (Has("AirPods", "Buds", "WF-", "Earbuds", "イヤホン")) return BatteryDeviceKind.Earbuds;
        if (Has("Controller", "Gamepad", "Pro Controller", "DualSense", "DUALSHOCK")) return BatteryDeviceKind.Gamepad;
        if (Has("Mouse", "マウス")) return BatteryDeviceKind.Mouse;
        if (Has("Keyboard", "キーボード")) return BatteryDeviceKind.Keyboard;
        return null;
    }

    public static BatteryDeviceKind Detect(string name, string? configured = null,
        IEnumerable<string>? compatibleIds = null, uint classOfDevice = 0)
    {
        if (TryParse(configured, out var selected)) return selected;
        if (FromHid(compatibleIds ?? []) is { } hid) return hid;
        var cod = FromClassOfDevice(classOfDevice);
        var named = FromName(name);
        if (cod == BatteryDeviceKind.Headphones && named == BatteryDeviceKind.Earbuds) return named.Value;
        return cod ?? named ?? BatteryDeviceKind.Other;
    }

    public static BatteryDevice Override(BatteryDevice device, BatterySettings settings) =>
        settings.Kinds.TryGetValue(device.Name, out string? value) && TryParse(value, out var kind)
            ? device with { Kind = kind } : device;
}
