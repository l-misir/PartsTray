// AirPods が近くの iPhone 向けに飛ばしている電波（Apple の「近くのペアリング」の知らせ）から、電池の残量を読み解きます。
// 読み解き方を変えるときはここを編集します。OS にはアクセスしない純粋な関数なので、テストで確かめられます。
namespace PartsTray.Core;

// 左右とケースの残量（0〜100、10% きざみ。分からないときは null）と、それぞれが充電中かどうか。Model は型番（記録用）。
public sealed record AirPodsReading(ushort Model, int? Left, int? Right, int? Case, bool LeftCharging, bool RightCharging, bool CaseCharging);

public static class AirPods
{
    public const ushort AppleCompanyId = 0x004C;   // 電波の「製造元のデータ」の会社 ID（Apple）

    // data は、会社 ID（0x004C）の後ろのバイト列。並びは公開されている解析（OpenPods・AirPodsDesktop）に合わせた：
    //   [0] 種類 0x07（近くのペアリング）  [1] 残りの長さ  [2] 0x01  [3..4] 型番  [5] 状態
    //   [6] 左右の残量（上下 4 bit ずつ）  [7] 上位 4 bit：充電中の印、下位 4 bit：ケースの残量
    // 残量の 4 bit は 0〜10 が 0〜100%、それ以外（15 など）は分からない（ケースの外で離れているなど）。
    // [5] の上位 4 bit の「2 の位」が 0 のときは、左右が入れ替わっている（主になっている側が逆）。
    // 11/20 実機：AirPods 4（型番 0x1B20）で、この並びのまま「左 100%・右 100%（どちらも充電中）・ケース 90%」と読めた。
    // 左右のイヤホンがそれぞれ別のアドレスで同じ内容を飛ばしていた（状態の入れ替わりの印だけが違う）。
    public static AirPodsReading? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || data[0] != 0x07 || data[1] < 6) return null;
        ushort model = (ushort)(data[3] << 8 | data[4]);
        bool flipped = ((data[5] >> 4) & 0x02) == 0;
        int high = data[6] >> 4, low = data[6] & 0x0F;
        int charge = data[7] >> 4;
        static int? Percent(int v) => v <= 10 ? v * 10 : null;
        return new AirPodsReading(model,
            Left: Percent(flipped ? high : low), Right: Percent(flipped ? low : high), Case: Percent(data[7] & 0x0F),
            LeftCharging: (charge & (flipped ? 0b010 : 0b001)) != 0, RightCharging: (charge & (flipped ? 0b001 : 0b010)) != 0,
            CaseCharging: (charge & 0b100) != 0);
    }

    // アイコンに出す 1 つの値：左右のうち少ない方（片方しか分からなければその値。どちらも分からなければケース）。
    // 説明文の 2 行目に、左右とケースを分けて出す。10% きざみなので「概算」として扱う。
    public static BatteryReading? ToReading(AirPodsReading r)
    {
        int? percent = (r.Left, r.Right) switch
        {
            ({ } left, { } right) => Math.Min(left, right),
            ({ } left, null) => left,
            (null, { } right) => right,
            _ => r.Case
        };
        if (percent is not { } value) return null;
        bool charging = (r.Left is not null && r.LeftCharging) || (r.Right is not null && r.RightCharging);
        return new BatteryReading(value, BatteryLevel.Unknown, charging ? BatteryCharge.Charging : BatteryCharge.Discharging,
            false, true, Detail(r));
    }

    public static string Detail(AirPodsReading r)
    {
        static string Part(string name, int? percent, bool charging) =>
            $"{name} {(percent is { } p ? $"{p}%" : "--")}{(percent is not null && charging ? "（充電中）" : "")}";
        return $"{Part("左", r.Left, r.LeftCharging)}・{Part("右", r.Right, r.RightCharging)}・{Part("ケース", r.Case, r.CaseCharging)}";
    }
}
