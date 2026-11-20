// 電池の値と機器の種類、残量の解釈・説明文を定義します。
// 電池の状態やツールチップの文を変える場所です。
namespace PartsTray.Core;

public enum BatteryDeviceKind { Other, Mouse, Keyboard, Headphones, Earbuds, Gamepad }
public enum BatteryCharge { Unknown, Discharging, Charging, SlowCharging, Full, Error }
[Flags] public enum BatteryLevel : byte { Unknown = 0, Critical = 1, Low = 2, Good = 4, Full = 8 }
// Detail：説明文の 2 行目に出す細かい内訳（AirPods の「左 80%・右 90%・ケース 50%」など）。ない機器は null。
public sealed record BatteryReading(int Percent, BatteryLevel Level, BatteryCharge Charge, bool ExternalPower, bool Approximate, string? Detail = null);
public sealed record BatteryDevice(string Id, string Name, BatteryDeviceKind Kind, bool Connected,
    BatteryReading? Reading, DateTime? UpdatedAt);

public static class BatteryMath
{
    public static BatteryReading? Decode(ReadOnlySpan<byte> p, bool unified)
    {
        if (p.Length < (unified ? 4 : 3) || p[0] > 100) return null;
        var level = unified ? (BatteryLevel)(p[1] & 15) : BatteryLevel.Unknown;
        int percent = p[0];
        bool approximate = unified && percent == 0 && level != BatteryLevel.Unknown;
        // 要確認: 複数のレベル bit が立つ未知の機器では、安全側の最も低いレベルを概算に使う。
        if (approximate) percent = (level & BatteryLevel.Critical) != 0 ? 5 : (level & BatteryLevel.Low) != 0 ? 20 : (level & BatteryLevel.Good) != 0 ? 50 : 90;
        var charge = p[2] switch
        {
            0 => BatteryCharge.Discharging, 1 => BatteryCharge.Charging,
            2 => unified ? BatteryCharge.SlowCharging : BatteryCharge.Charging,
            3 => BatteryCharge.Full, 4 when unified => BatteryCharge.Error, _ => BatteryCharge.Unknown
        };
        return new(percent, level, charge, unified && p[3] != 0, approximate);
    }

    public static string BluetoothName(string name)
    {
        // 「 - Find My」は、Apple の機器が「探す」に対応していることを示す名前の後ろの印（AirPods 4 で確認）。
        foreach (string suffix in new[] { " Hands-Free AG", " Hands-Free", " Avrcp Transport", " - Find My" })
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return name[..^suffix.Length];
        return name;
    }

    public static string Tooltip(BatteryDevice device)
    {
        string text = $"{device.Name} {(device.Reading is { } r ? $"{r.Percent}%" : "--")}";
        if (!device.Connected) text += $"（切断中・{device.UpdatedAt?.ToLocalTime():HH:mm} 時点）";
        else text += device.Reading?.Charge switch
        {
            BatteryCharge.Charging => "（充電中）", BatteryCharge.SlowCharging => "（ゆっくり充電中）",
            BatteryCharge.Full => "（充電完了）", BatteryCharge.Error => "（充電エラー）", _ => ""
        };
        if (device.Reading?.Approximate == true) text += "（概算）";
        if (device.Reading?.Detail is { Length: > 0 } detail) text += "\n" + detail;
        return text.Length <= 127 ? text : text[..127];
    }

    // 輪の色。11/20 ユーザーの指定：輪の色は残量だけで決める（充電中かどうかでは変えない。充電中は稲妻の有無で見せる）。
    // 普段は緑、lowWarn 以下はオレンジ（稲妻の黄色とかぶらないよう、黄色から変えた）、lowHot 以下は赤。
    public static (byte R, byte G, byte B) RingColor(int? percent, int lowWarn, int lowHot) =>
        Rgb(percent <= lowHot ? Palette.Red : percent <= lowWarn ? Palette.Orange : Palette.Green);

    private static (byte R, byte G, byte B) Rgb(System.Drawing.Color c) => (c.R, c.G, c.B);

    // 稲妻（充電中の印）を出すか。ケーブルがつながったまま満タン（充電完了）も含める（iPhone と同じ）。
    public static bool ShowsBolt(BatteryReading? reading) =>
        reading?.Charge is BatteryCharge.Charging or BatteryCharge.SlowCharging or BatteryCharge.Full;

    // 11/20 ユーザーの指定：肩代わりの状態（「肩代わり: 肩代わり中」など。肩代わりを切っているときは空）は、マウスの電池のアイコンの説明に出す。
    // 状態は 2 行目。NotifyIcon の説明は 127 文字までなので、はみ出す分は切る。
    public static string Tooltip(BatteryDevice device, string? status)
    {
        string text = Tooltip(device);
        if (!string.IsNullOrEmpty(status)) text += "\n" + status;
        return text.Length <= 127 ? text : text[..127];
    }
}
