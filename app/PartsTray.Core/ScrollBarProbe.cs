// 元のカーソル位置と内側の判定点を組にし、ホイールへ渡す結果を常に元の位置に結び付けます。
namespace PartsTray.Core;

public readonly record struct NativeScrollCheck(bool Hit, string Result, long ChromiumWindow = 0);

public readonly record struct ScrollBarProbe(PixelPoint Cursor, PixelPoint? Inside, PixelPoint? InputPosition = null)
{
    public (NativeScrollCheck Direct, NativeScrollCheck? Inward) CheckNative(PointerWindowPolicy windows, Func<int> remaining)
    {
        var direct = windows.CheckScrollBar(Cursor, remaining);
        var inward = !direct.Hit && Inside is { } point ? windows.CheckScrollBar(point, remaining) : (NativeScrollCheck?)null;
        return (direct, inward);
    }

    // 内側の座標を、実際に Chromium と判定した窓へ送る。判定中に窓が変わっていたら使わない。
    public EdgeWheelTarget ChromiumTarget(PixelPoint? foundAt, long renderer, long source, long target)
    {
        if (Inside is not { } inside) return default;
        var destination = new EdgeWheelTarget(renderer, inside);
        return destination.CanPost && (foundAt == Cursor && renderer == source || foundAt == inside && renderer == target)
            ? destination : default;
    }

    // 初回は窓ごとに先読みしたクライアント領域を使う。元と内側の優先順は通常の判定と同じ。
    public EdgeWheelTarget FirstChromiumTarget(long source, ScrollBarBounds? direct, long target,
        ScrollBarBounds? inward, int scrollBarWidth)
    {
        if (Inside is not { } inside) return default;
        if (direct?.InRightBand(Cursor, scrollBarWidth) == true)
            return ChromiumTarget(Cursor, source, source, target);
        return inward?.InRightBand(inside, scrollBarWidth) == true
            ? ChromiumTarget(inside, target, source, target) : default;
    }

    public PointerCache ToPointerCache(IClock clock, PixelPoint? foundAt, long disabled = 0,
        PixelPoint origin = default, EdgeWheelTarget redirect = default, EdgeWheelTarget chromiumEdge = default)
        => new(InputPosition ?? Cursor, clock.Now, foundAt == Cursor, disabled, origin,
            Inside.HasValue && foundAt == Inside, redirect, chromiumEdge);
}
