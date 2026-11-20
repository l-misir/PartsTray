// 電池アイコンを種類固定の枠と追加の枠へ割り当てます。
// 表示条件・並び順・台数を変える場所です。トレイ操作は行いません。
namespace PartsTray.Core;

public static class BatteryLayout
{
    public const int SlotCount = 12;
    public static int KindSlot(BatteryDeviceKind kind) => kind switch
    {
        BatteryDeviceKind.Mouse => 0, BatteryDeviceKind.Keyboard => 1,
        BatteryDeviceKind.Headphones => 2, BatteryDeviceKind.Earbuds => 3,
        BatteryDeviceKind.Gamepad => 4, _ => 5
    };

    public static BatteryDevice?[] Assign(IEnumerable<BatteryDevice> devices, BatterySettings settings)
    {
        var slots = new BatteryDevice?[SlotCount];
        int spare = 6;
        // 要確認: 同名の別機器は ID 順で決め、検出順で主枠が入れ替わらないようにする。
        foreach (var d in devices.Select(d => BatteryKinds.Override(d, settings))
            .Where(d => settings.Enabled && d.Reading != null &&
                (d.Id.StartsWith("logitech:", StringComparison.Ordinal) ? settings.Logitech : settings.Bluetooth) &&
                !settings.Hidden.Contains(d.Name, StringComparer.Ordinal) &&
                (d.Connected || d.Kind is not (BatteryDeviceKind.Headphones or BatteryDeviceKind.Earbuds or BatteryDeviceKind.Gamepad)))
            .OrderBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            int primary = KindSlot(d.Kind);
            if (slots[primary] is null) slots[primary] = d;
            else if (spare < slots.Length) slots[spare++] = d;
        }
        return slots;
    }
}
