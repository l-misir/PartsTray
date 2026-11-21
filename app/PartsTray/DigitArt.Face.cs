// 設定した書体・太さ・描き方を、同梱フォントまたは Windows のフォントに対応付けます。
using PartsTray.Core;

namespace PartsTray;

internal static partial class DigitArt
{
    internal readonly record struct DigitFace(string Family, FontStyle Weight, DigitRendering? Rendering = null,
        bool PlainZero = true, bool Square2 = false);
    private static readonly Dictionary<(string Name, FontStyle DefaultWeight), DigitFace> faces = [];

    internal static DigitFace ResolveFont(string? name, FontStyle defaultWeight = FontStyle.Bold)
    {
        name = string.IsNullOrWhiteSpace(name) ? MonitorSettings.DefaultDigitFont : name.Trim();
        lock (gate)
        {
            var key = (name, defaultWeight);
            if (faces.TryGetValue(key, out var cached)) return cached;
            var spec = DigitFontSpec.Parse(name);
            FontStyle? explicitWeight = spec.Weight switch
            {
                DigitWeight.Regular => FontStyle.Regular,
                DigitWeight.Bold => FontStyle.Bold,
                _ => null
            };
            if (TerminusDigits.WeightFor(spec.Family) is { } nativeWeight)
                return faces[key] = new DigitFace("Terminus", explicitWeight ?? nativeWeight, spec.Rendering, Square2: spec.Square2);

            bool bold = spec.Family.EndsWith(" Bold", StringComparison.OrdinalIgnoreCase);
            bool semibold = spec.Family.EndsWith(" Semibold", StringComparison.OrdinalIgnoreCase);
            // 旧形式の Semibold は独立したファミリの標準ウェイト。旧形式の細字は描画経路の太さに従う。
            FontStyle fallbackWeight = explicitWeight ?? (spec.Rendering.HasValue ? FontStyle.Bold : defaultWeight);
            FontStyle weight = explicitWeight ?? (bold || semibold ? FontStyle.Regular : fallbackWeight);
            var face = InstalledFace(spec.Family, weight);
            if (face == null && bold) face = InstalledFace(spec.Family[..^5], explicitWeight ?? FontStyle.Bold);
            face ??= new DigitFace(MonitorSettings.DefaultDigitFont, fallbackWeight);
            return faces[key] = face.Value with { Rendering = spec.Rendering, Square2 = spec.Square2 };
        }
    }

    // Font(string, ...) は見つからない名前を黙って代替するため、ファミリと太さを先に確認する。
    private static DigitFace? InstalledFace(string name, FontStyle weight)
    {
        try
        {
            using var family = new FontFamily(name);
            return family.IsStyleAvailable(weight) ? new DigitFace(family.Name, weight) : null;
        }
        catch (ArgumentException) { return null; }
    }
}
