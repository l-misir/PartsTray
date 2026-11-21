// USB でつないだ Android などの機器が、ファイルのやり取りの仕組み（WPD・MTP）で知らせる電池の値を、電池の表示の形に直します。
// 充電中の扱いや機器の種類の見分け方を変えるときはここを編集します。OS にはアクセスしない純粋な関数です（読む部分は PartsTray/PortableDeviceBattery.cs）。
namespace PartsTray.Core;

public static class PortableBattery
{
    // 充電中は、機器が「外部電源あり」と言っているだけで、本当に充電しているかは分からないので、説明にそう書く。
    public const string ChargingNote = "PC から電源あり（充電中とみなす）";

    // level：WPD_DEVICE_POWER_LEVEL（0〜100）。source：WPD_DEVICE_POWER_SOURCE（0＝電池、1＝外部電源）。
    // 残量のない機器（iPhone・iPad など）や、おかしな値は null。
    public static BatteryReading? ToReading(uint? level, uint? source)
    {
        if (level is not { } percent || percent > 100) return null;
        bool external = source == 1;
        if (!external) return new BatteryReading((int)percent, BatteryLevel.Unknown, BatteryCharge.Discharging, false, false);
        return percent >= 100
            ? new BatteryReading(100, BatteryLevel.Unknown, BatteryCharge.Full, true, false)
            : new BatteryReading((int)percent, BatteryLevel.Unknown, BatteryCharge.Charging, true, false, ChargingNote);
    }

    // WPD_DEVICE_TYPE：0＝汎用、3＝電話。Android はどちらかで知らせる。カメラ・音楽プレーヤーなどは「その他」。
    // タブレットは電話と区別できないので、設定画面で機器ごとに選び直してもらう。
    public static BatteryDeviceKind Kind(uint? type) => type is null or 0 or 3 ? BatteryDeviceKind.Phone : BatteryDeviceKind.Other;

    // iPhone・iPad は Apple のサービス経由で読むので、こちらでは数えない（二重に出さない）。
    public static bool IsApple(string? manufacturer) => manufacturer?.Contains("Apple", StringComparison.OrdinalIgnoreCase) == true;
}
