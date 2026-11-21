// 同梱した Terminus の数字と欠測記号を原寸で読み、表示枠に収まる最大の原寸を選びます。
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal static class TerminusDigits
{
    // Terminus TTF 4.49.3 の EBLC にある、正方ピクセルの 1 bit ビットマップの原寸。
    private static readonly int[] nativeSizes = [12, 14, 16, 18, 20, 22, 24, 28, 32];
    private sealed record Strike(byte[][,] Digits, byte[,] PlainZeroInk, byte[,] Missing);

    private static readonly Dictionary<(FontStyle Weight, bool Smooth), Dictionary<int, Strike>> strikes = [];
    private static readonly Dictionary<(FontStyle Weight, int Width, int Height, int Maximum, bool Smooth, bool PlainZero),
        PackedDigits<byte>?> layouts = [];

    internal static FontStyle? WeightFor(string? name)
    {
        name = name?.Trim();
        if (string.Equals(name, "Terminus", StringComparison.OrdinalIgnoreCase)) return FontStyle.Regular;
        if (string.Equals(name, "TerminusBold", StringComparison.OrdinalIgnoreCase)) return FontStyle.Bold;
        return null;
    }

    internal static PackedDigits<byte>? FindLayout(FontStyle weight, int width, int height, int maximumValue,
        DigitRendering? rendering, bool plainZero)
    {
        var key = (weight, width, height, maximumValue, rendering == DigitRendering.Smooth, plainZero);
        lock (layouts)
        {
            if (layouts.TryGetValue(key, out var cached)) return cached;
            var bySize = Strikes(weight, rendering);
            return layouts[key] = PackedDigits<byte>.FitNative(width, height, maximumValue, nativeSizes, (digit, pixels) =>
            {
                var strike = bySize[(int)pixels];
                return digit == '0' && plainZero ? strike.PlainZeroInk : strike.Digits[digit - '0'];
            });
        }
    }

    internal static byte[,] Missing(FontStyle weight, DigitRendering? rendering, PackedDigits<byte> layout)
    {
        lock (layouts) return Strikes(weight, rendering)[(int)layout.Pixels].Missing;
    }

    private static Dictionary<int, Strike> Strikes(FontStyle weight, DigitRendering? rendering)
    {
        var key = (weight, rendering == DigitRendering.Smooth);
        if (strikes.TryGetValue(key, out var cached)) return cached;
        return strikes[key] = ReadStrikes(weight, rendering);
    }

    private static Dictionary<int, Strike> ReadStrikes(FontStyle weight, DigitRendering? rendering)
    {
        string file = weight == FontStyle.Bold ? "TerminusTTF-Bold-4.49.3.ttf" : "TerminusTTF-4.49.3.ttf";
        using var resource = typeof(TerminusDigits).Assembly.GetManifestResourceStream($"PartsTray.Fonts.{file}")
            ?? throw new InvalidDataException($"同梱フォント {file} が見つかりません。");
        var bytes = new byte[checked((int)resource.Length)];
        resource.ReadExactly(bytes);

        // AddMemoryFont の元データはフォントとコレクションを破棄するまで固定する。
        // 数字をマスクに写し終えたら、GDI+ のオブジェクトも固定メモリも不要になる。
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            using var collection = new PrivateFontCollection();
            collection.AddMemoryFont(pinned.AddrOfPinnedObject(), bytes.Length);
            using var family = collection.Families.Single();
            if (family.Name != "Terminus (TTF)" || !family.IsStyleAvailable(weight))
                throw new InvalidDataException($"同梱フォント {file} の書体・太さを読み込めません。");

            var strikes = new Dictionary<int, Strike>();
            foreach (int pixels in nativeSizes)
            {
                using var font = new Font(family, pixels, weight, GraphicsUnit.Pixel);
                int side = pixels * 2 + 8;
                byte[,] Read(string text) => DigitArt.Rasterize(text, font, side, side, 4, rendering, plainZero: false);
                var digits = "0123456789".Select(digit => Read(digit.ToString())).ToArray();
                var missing = DigitArt.TrimToRows(Read("--"), DigitArt.InkRows(digits[0]));
                strikes.Add(pixels, new Strike(digits, PlainZero.Clean(digits[0]), missing));
            }
            return strikes;
        }
        finally { pinned.Free(); }
    }
}
