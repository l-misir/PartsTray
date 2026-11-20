// 残量だけが分かる機器（Windows から読む Bluetooth 機器）の、充電中かどうかを残量の増え方から推測します。
// 「充電中とみなす」条件や、みなすのをやめるまでの時間を変えるときはここを編集します。OS にはアクセスしません。
namespace PartsTray.Core;

// Windows は Bluetooth 機器の「残量」しか持っておらず、充電中かどうかは分からない（11/20 実機：WH-1000XM6 の部品の
// どこにも充電の情報がなかった）。そこで、残量が前より増えたら充電中とみなす（11/20 ユーザーの指定：
// 「充電増えたら充電中にする仕様にしておいて」）。
//   ・前より増えた → 充電中（100% なら充電完了）
//   ・前より減った → 充電中ではない
//   ・同じ → 最後に増えてから HoldMinutes 分のあいだは充電中のまま。過ぎたら充電中ではない
//   ・切断したら覚えていた残量を忘れる（切れている間に充電して戻ってきたときに、充電中と見間違えないため）
public sealed class BatteryTrend
{
    public const int HoldMinutes = 20;
    private readonly Dictionary<string, (int Percent, DateTime? RoseAt)> last = new(StringComparer.Ordinal);

    // 推測した結果。分からないときは Unknown（稲妻を出さない）。
    public BatteryCharge Update(string id, int percent, bool connected, DateTime now)
    {
        if (!connected) { last.Remove(id); return BatteryCharge.Unknown; }
        DateTime? roseAt = null;
        if (last.TryGetValue(id, out var before))
        {
            if (percent > before.Percent) roseAt = now;
            else if (percent == before.Percent && before.RoseAt is { } t && now - t < TimeSpan.FromMinutes(HoldMinutes)) roseAt = t;
        }
        last[id] = (percent, roseAt);
        return roseAt is null ? BatteryCharge.Unknown : percent >= 100 ? BatteryCharge.Full : BatteryCharge.Charging;
    }

    // 説明文の 2 行目に出す断り書き（推測だと分かるように）。
    public const string Note = "残量が増えたので、充電中とみなしています";
}
