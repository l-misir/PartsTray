// 設定した点の絵をアイコンの枠に合わせます。設定を読み直したときは古い PNG の配置も解放できます。
using System.Runtime.CompilerServices;
using PartsTray.Core;

namespace PartsTray;

internal static class DotDigitArt
{
    private static readonly string[][][] coreTemp = [DotDigitSets.CoreTemp96, DotDigitSets.CoreTemp120];
    private static readonly string[][][] claude = [DotDigitSets.Claude, DotDigitSets.ClaudeSmall];
    private static readonly ConditionalWeakTable<string[][][], Dictionary<(int Width, int Height, int? Extra), DotDigitLayout?>> layouts = new();

    internal static byte[,] Draw(DotDigitKind kind, string[][][]? fileDigits, int? value, int width, int height)
    {
        if (kind is DotDigitKind.SevenSeg or DotDigitKind.SevenSegNarrow)
            return SevenSegmentDigits.Draw(value, width, height, narrow: kind == DotDigitKind.SevenSegNarrow);
        var sources = kind switch
        {
            DotDigitKind.Claude => claude,
            DotDigitKind.File when fileDigits != null => fileDigits,
            _ => coreTemp
        };
        int? extra = value is < 0 or > 150 ? value : null;
        var layout = Fit(sources, width, height, extra);
        if (layout == null && kind == DotDigitKind.File)
            layout = Fit(coreTemp, width, height, extra);
        return layout?.Draw(value) ?? new byte[0, 0];
    }

    private static DotDigitLayout? Fit(string[][][] sets, int width, int height, int? extra)
    {
        var byFrame = layouts.GetOrCreateValue(sets);
        lock (byFrame)
        {
            if (!byFrame.TryGetValue((width, height, extra), out var layout))
                byFrame[(width, height, extra)] = layout = DotDigitLayout.Fit(sets, width, height, extra);
            return layout;
        }
    }
}
