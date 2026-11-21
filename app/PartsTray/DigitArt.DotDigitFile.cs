// digits.png の透明度を読み取り、数字の点に変換したら画像とファイルを閉じます。
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal static class DotDigitFile
{
    internal static string[][][] Load(string path, Action<string> log) => DotDigitSheet.Load(path, Decode, log);

    private static string[][] Decode(Stream stream)
    {
        try
        {
            using var bitmap = (Bitmap)Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return DotDigitSheet.Read(bitmap.Width, bitmap.Height, (x, y) => bitmap.GetPixel(x, y).A);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            // GDI+ は画像データが途中で壊れている場合にも OutOfMemoryException を返す。
            throw new InvalidDataException("PNG の画像データを展開できません。", ex);
        }
    }
}
