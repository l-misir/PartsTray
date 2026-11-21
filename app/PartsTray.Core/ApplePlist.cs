// iPhone・iPad とのやり取りに使う Apple の「plist」（XML 形式）を組み立て・読み解き、電池の値に直します。
// 形式や電池の読み方を変えるときはここを編集します。OS にはアクセスしない純粋な関数です。
using System.Globalization;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace PartsTray.Core;

public static class ApplePlist
{
    // 値は Dictionary<string, object?>・List<object?>・string・long・double・bool・byte[] のどれか。
    public static string Write(Dictionary<string, object?> root) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">" +
        Node(root) + "</plist>";

    private static string Node(object? v) => v switch
    {
        Dictionary<string, object?> d => "<dict>" + string.Concat(d.Select(kv => $"<key>{SecurityElement.Escape(kv.Key)}</key>{Node(kv.Value)}")) + "</dict>",
        List<object?> list => "<array>" + string.Concat(list.Select(Node)) + "</array>",
        string s => $"<string>{SecurityElement.Escape(s)}</string>",
        long or int => $"<integer>{Convert.ToInt64(v, CultureInfo.InvariantCulture)}</integer>",
        bool b => b ? "<true/>" : "<false/>",
        byte[] bytes => $"<data>{Convert.ToBase64String(bytes)}</data>",
        _ => "<string></string>"
    };

    // XML の plist を読む（いちばん外側は dict）。バイナリの plist（bplist）は扱わない（受け取ったら例外）。
    public static Dictionary<string, object?> Read(byte[] data)
    {
        if (data.Length >= 6 && Encoding.ASCII.GetString(data, 0, 6) == "bplist")
            throw new NotSupportedException("バイナリの plist は読めません");
        var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
        return Value(doc.Root!.Elements().First()) as Dictionary<string, object?> ?? throw new InvalidDataException("plist の外側が dict ではありません");
    }

    private static object? Value(XElement e) => e.Name.LocalName switch
    {
        "dict" => e.Elements().Chunk(2).Where(p => p.Length == 2).ToDictionary(p => p[0].Value, p => Value(p[1])),
        "array" => e.Elements().Select(Value).ToList(),
        "string" => e.Value,
        "integer" => long.Parse(e.Value, CultureInfo.InvariantCulture),
        "real" => double.Parse(e.Value, CultureInfo.InvariantCulture),
        "true" => true, "false" => false,
        "data" => Convert.FromBase64String(string.Concat(e.Value.Where(c => !char.IsWhiteSpace(c)))),
        "date" => e.Value,
        _ => null
    };

    // lockdownd の「com.apple.mobile.battery」の値を電池の値に直す（11/20 実機：iPhone SE 第 3 世代・iPad で確認）。
    //   BatteryCurrentCapacity（残量 %）、BatteryIsCharging（充電中）、FullyCharged（満充電）、ExternalConnected（電源につながっている）
    public static BatteryReading? Battery(Dictionary<string, object?> value)
    {
        if (value.GetValueOrDefault("BatteryCurrentCapacity") is not long percent || percent is < 0 or > 100) return null;
        bool charging = value.GetValueOrDefault("BatteryIsCharging") is true;
        bool full = value.GetValueOrDefault("FullyCharged") is true;
        bool external = value.GetValueOrDefault("ExternalConnected") is true;
        var charge = full && external ? BatteryCharge.Full : charging ? BatteryCharge.Charging : external ? BatteryCharge.Unknown : BatteryCharge.Discharging;
        return new BatteryReading((int)percent, BatteryLevel.Unknown, charge, external, false);
    }
}
