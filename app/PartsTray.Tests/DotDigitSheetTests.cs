// digits.png の枠とアルファの解釈、読めないときの記録と CoreTemp 原画への復帰を検証します。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DotDigitSheetTests
{
    private const int Width = 43; // 3 列の枠 11 個と透明な区切り 10 列
    private const int Height = 6;

    private static byte Alpha(int x, int y)
    {
        // 字ごとに点が別の行にある。左右の余白を削ってもこの位置関係を保つ。
        int cell = x / 4, local = x % 4;
        if (local == cell % 3 && y == 1 + cell % 4) return 255;
        return local == 1 && y == 2 ? (byte)128 : (byte)0;
    }

    [Fact]
    public void ReadsOnlyOpaquePixelsAndPreservesSharedVerticalPositions()
    {
        var glyphs = DotDigitSheet.Read(Width, Height, Alpha);
        Assert.Equal(11, glyphs.Length);
        for (int i = 0; i < glyphs.Length; i++)
        {
            var expected = new[] { ".", ".", ".", "." };
            expected[i % 4] = "#";
            Assert.Equal(expected, glyphs[i]);
        }
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("noCells")]
    [InlineData("separator")]
    [InlineData("missingDigit")]
    public void RejectsMalformedOrIncompleteStrips(string error)
    {
        int width = error switch { "width" => Width + 1, "noCells" => 10, _ => Width };
        int height = error == "height" ? 0 : Height;
        byte Pixel(int x, int y)
        {
            if (error == "separator" && x == 39 && y == 0) return 1;
            return error == "missingDigit" && x >= 40 ? (byte)0 : Alpha(x, y);
        }
        Assert.Throws<InvalidDataException>(() => DotDigitSheet.Read(width, height, Pixel));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrongSignature")]
    [InlineData("truncated")]
    [InlineData("decodeError")]
    public void ReportsUnreadableSheetAndReturnsBothCoreTempSources(string error)
    {
        string path = Path.Combine(Path.GetTempPath(), $"digits-{Guid.NewGuid():N}.png");
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        try
        {
            if (error != "missing")
                File.WriteAllBytes(path, error switch
                {
                    "wrongSignature" => new byte[8], "truncated" => signature[..4], _ => signature
                });
            var messages = new List<string>();
            bool called = false;
            var fallback = DotDigitSheet.Load(path, _ =>
            {
                called = true;
                throw new InvalidDataException("数字画像が途中で終わりました");
            }, messages.Add);
            Assert.Equal(error == "decodeError", called);
            Assert.False(string.IsNullOrWhiteSpace(Assert.Single(messages)));
            Assert.Collection(fallback,
                small => Assert.Same(DotDigitSets.CoreTemp96, small),
                large => Assert.Same(DotDigitSets.CoreTemp120, large));
        }
        finally { File.Delete(path); }
    }
}
