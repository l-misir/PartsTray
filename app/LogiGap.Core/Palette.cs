// アイコンで共用する色を名前で定義します。
// 色の選択や背景とのコントラストの規則を変える場所です。
using System.Drawing;
namespace LogiGap.Core;

public static class Palette
{
    public static readonly Color White = Color.FromArgb(255, 255, 255);
    public static readonly Color Cyan = Color.FromArgb(100, 210, 255);
    public static readonly Color Red = Color.FromArgb(255, 59, 48);
    public static readonly Color Green = Color.FromArgb(52, 199, 89);
    public static readonly Color Orange = Color.FromArgb(255, 149, 0);
    public static readonly Color Bolt = Color.FromArgb(255, 214, 10);
    public static readonly Color DarkBackground = Color.FromArgb(28, 28, 28);
    // 要確認: 明るいタスクバーの背景色は実機未測定のため #F3F3F3 を使う。
    public static readonly Color LightBackground = Color.FromArgb(243, 243, 243);

    public static Color Background(bool light) => light ? LightBackground : DarkBackground;

    public static double Contrast(Color first, Color second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Linear(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    // 明るいタスクバーで白・灰色の代わりに使う色（Windows の明るいテーマの記号と同じく、ほぼ黒）。
    public static readonly Color LightForeground = Color.FromArgb(26, 26, 26);

    public static Color ForTaskbar(Color color, bool light)
    {
        Color background = Background(light);
        if (Contrast(color, background) >= 3) return color;
        // 11/20 Claude：白（機器の絵・メモリの数字など）は、3:1 ぎりぎりの灰色だとくすんで見えるので、ほぼ黒にする。
        // 色の付いたもの（輪・稲妻・CPU の水色など）は、色合いを保ったまま最小限だけ暗くする（下の計算）。
        if (light && color.GetSaturation() == 0) return Color.FromArgb(color.A, LightForeground);
        // 要確認: 色合いと彩度を固定した HSL の明度を二分探索し、8 bit 化後も 3:1 を満たす最小の補正で止める。
        double low = 0, high = 1, start = color.GetBrightness(), end = light ? 0 : 1;
        Color best = light ? Color.FromArgb(color.A, 0, 0, 0) : Color.FromArgb(color.A, 255, 255, 255);
        for (int i = 0; i < 40; i++)
        {
            double amount = (low + high) / 2;
            Color next = FromHsl(color.A, color.GetHue(), color.GetSaturation(), start + (end - start) * amount);
            if (Contrast(next, background) >= 3) { best = next; high = amount; }
            else low = amount;
        }
        return best;
    }

    private static Color FromHsl(byte alpha, double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double h = hue / 60, x = chroma * (1 - Math.Abs(h % 2 - 1)), m = lightness - chroma / 2;
        var (r, g, b) = h switch
        {
            < 1 => (chroma, x, 0.0), < 2 => (x, chroma, 0.0), < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma), < 5 => (x, 0.0, chroma), _ => (chroma, 0.0, x)
        };
        int Byte(double v) => (int)Math.Clamp(Math.Round((v + m) * 255), 0, 255);
        return Color.FromArgb(alpha, Byte(r), Byte(g), Byte(b));
    }

    // 暗い背景で輪を明るくする既存の計算。明るい背景では呼ばない。
    public static Color Brighten(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        if (max == 0) return c;
        double f = 255.0 / max;
        return Color.FromArgb(c.A, (int)Math.Min(255, Math.Round(c.R * f)), (int)Math.Min(255, Math.Round(c.G * f)), (int)Math.Min(255, Math.Round(c.B * f)));
    }
}
