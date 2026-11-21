// 試し表示の数字を全アイコンで共有し、一覧の末尾から先頭に戻します。
namespace PartsTray.Core;

public sealed class DigitPreview
{
    private int[] values = [];
    private int index;

    public bool Enabled => values.Length != 0;
    public int? Display(int? measured) => Enabled ? values[index] : measured;

    // 他の設定だけを読み直したときは、表示中の数字と残り時間を保つ。
    public bool SetValues(int[] next)
    {
        if (values.AsSpan().SequenceEqual(next)) return false;
        values = next;
        index = 0;
        return true;
    }

    public void Advance()
    {
        if (Enabled) index = (index + 1) % values.Length;
    }
}
