// アイコンの種類から設定画面のタブと欄を決めます。
// アイコンの入口や移動先を変えるときに開きます。
namespace PartsTray.Core;

public enum SettingsIconKind { Other, Parrot, Cpu, Temperature, Memory, Battery }
public sealed record SettingsDestination(string? Tab = null, string? Field = null, string? DeviceName = null);
public static class SettingsNavigation
{
    public static SettingsDestination Resolve(SettingsIconKind kind, string? deviceName = null) => kind switch
    {
        SettingsIconKind.Parrot => new("通知領域のアイコン", "オウムを出す"),
        SettingsIconKind.Cpu => new("通知領域のアイコン", "CPU 使用率のアイコンを出す"),
        SettingsIconKind.Temperature => new("通知領域のアイコン", "温度の読み方"),
        SettingsIconKind.Memory => new("通知領域のアイコン", "メモリの色"),
        SettingsIconKind.Battery => new("通知領域のアイコン",
            string.IsNullOrEmpty(deviceName) ? "電池のアイコンを出す" : "機器の種類", deviceName),
        _ => new()
    };
}
