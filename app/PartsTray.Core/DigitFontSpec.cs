// 通知領域の数字の書体名と、省略できる太さ・描き方・縦横比を設定文字列から読み取ります。
namespace PartsTray.Core;

public enum DigitWeight { Regular, Bold }
public enum DigitRendering { Smooth, Pixel }
public enum DotDigitKind { CoreTemp, Claude, SevenSeg, File, SevenSegNarrow }

// 太さや描き方が null の場合は、単独の書体名に対する従来の選び方を使う。
public readonly record struct DigitFontSpec(string Family, DigitWeight? Weight, DigitRendering? Rendering,
    bool Square2 = false)
{
    private static readonly Dictionary<string, DotDigitKind> dotSets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dots:CoreTemp"] = DotDigitKind.CoreTemp, ["Dots:Claude"] = DotDigitKind.Claude,
        ["Dots:SevenSeg"] = DotDigitKind.SevenSeg, ["Dots:File"] = DotDigitKind.File
    };

    // Dots: で始まる指定は固定の点の絵。各アイコンにも共通の書式を使う。
    public static DotDigitKind? DotSet(string? value, string field = "monitor.digitFont")
    {
        string? name = value?.Trim();
        if (name == null || !name.StartsWith("Dots:", StringComparison.OrdinalIgnoreCase)) return null;
        if (dotSets.TryGetValue(name, out var kind)) return kind;
        int comma = name.IndexOf(',');
        if (comma >= 0 && name.AsSpan(0, comma).TrimEnd().Equals("Dots:SevenSeg", StringComparison.OrdinalIgnoreCase))
        {
            var option = name.AsSpan(comma + 1).TrimStart();
            if (option.Equals("Narrow", StringComparison.OrdinalIgnoreCase)) return DotDigitKind.SevenSegNarrow;
            // 従来の Center は既定の字幅で描く。
            if (option.Equals("Center", StringComparison.OrdinalIgnoreCase)) return DotDigitKind.SevenSeg;
        }
        throw new InvalidDataException($"{field} の点の絵には Dots:CoreTemp / Dots:Claude / Dots:SevenSeg / Dots:SevenSeg, Narrow / Dots:File を指定してください");
    }

    public static DigitFontSpec Parse(string? value, string field = "monitor.digitFont")
    {
        if (string.IsNullOrWhiteSpace(value)) return new(MonitorSettings.DefaultDigitFont, null, null);
        var dots = DotSet(value, field);
        if (dots == DotDigitKind.SevenSeg) return new("Dots:SevenSeg", null, null);
        if (dots.HasValue || !value.Contains(',')) return new(value.Trim(), null, null);

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length > 4 || parts[0].Length == 0)
            throw new InvalidDataException($"{field} は「書体名, 太さ, 描き方, 縦横比」の形です");

        string weight = parts[1].ToLowerInvariant();
        string rendering = parts.Length >= 3 ? parts[2].ToLowerInvariant() : "";
        // 太さを省いた「Tahoma, Pixel」も「Tahoma, , Pixel」と同じにする。
        if (parts.Length == 2 && weight is ("smooth" or "pixel"))
        {
            rendering = weight;
            weight = "";
        }

        DigitWeight? parsedWeight = weight switch
        {
            "" => null,
            "regular" => DigitWeight.Regular,
            "bold" => DigitWeight.Bold,
            _ => throw new InvalidDataException($"{field} の太さは Regular / Bold のどちらかです")
        };
        DigitRendering? parsedRendering = rendering switch
        {
            "" => null,
            "smooth" => DigitRendering.Smooth,
            "pixel" => DigitRendering.Pixel,
            _ => throw new InvalidDataException($"{field} の描き方は Smooth / Pixel のどちらかです")
        };
        bool square2 = parts.Length == 4 && parts[3].Length != 0;
        if (square2 && !parts[3].Equals("Square2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{field} の縦横比には Square2 を指定するか、省略してください");
        return new(parts[0], parsedWeight, parsedRendering, square2);
    }
}
