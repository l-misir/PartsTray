// すべての数字のアイコンが同じ順序で切り替わり、解除すると実測値に戻ることを確かめます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class DigitPreviewTests
{
    [Fact]
    public void EachTickAdvancesAllReadingsTogetherAndWraps()
    {
        var preview = new DigitPreview();
        int[] values = [8, 37, 99, 100, 150, 999, 0];
        preview.SetValues(values);

        foreach (int expected in values.Append(8))
        {
            Assert.Equal(expected, preview.Display(42));
            Assert.Equal(expected, preview.Display(null));
            Assert.Equal(expected, preview.Display(67));
            preview.Advance();
        }
    }

    [Fact]
    public void EmptyListShowsCurrentReadingsIncludingMissingOnes()
    {
        var preview = new DigitPreview();
        Assert.False(preview.Enabled);
        Assert.Equal(7, preview.Display(7));
        preview.SetValues([100]);
        preview.Advance();
        Assert.Equal(100, preview.Display(null));

        preview.SetValues([]);
        preview.Advance();
        Assert.False(preview.Enabled);
        Assert.Equal(84, preview.Display(84));
        Assert.Null(preview.Display(null));
    }

    [Fact]
    public void OnlyChangingTheSequenceRestartsIt()
    {
        var preview = new DigitPreview();
        Assert.True(preview.SetValues([8, 37]));
        preview.Advance();
        Assert.False(preview.SetValues([8, 37]));
        Assert.Equal(37, preview.Display(0));
        Assert.True(preview.SetValues([150, 8]));
        Assert.Equal(150, preview.Display(0));
    }
}
