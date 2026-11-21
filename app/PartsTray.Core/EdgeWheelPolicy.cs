// モニターの端から調べる点と、ホイールを別の子ウィンドウに届けられる条件を決めます。
using System.Drawing;

namespace PartsTray.Core;

public readonly record struct EdgeWheelTarget(long Window, PixelPoint Position, uint ProcessId = 0)
{
    // WM_MOUSEWHEEL の lParam に入る画面座標は各軸とも符号付き16ビット。
    internal bool CanPost => Window != 0 &&
        Position.X is >= short.MinValue and <= short.MaxValue &&
        Position.Y is >= short.MinValue and <= short.MaxValue;

    public bool PostWheel(PointerEvent input, uint keys, Func<nint, uint, nuint, nint, bool> postMessage)
    {
        if (!CanPost) return false;
        uint message = input.Kind == MouseEventKind.HorizontalWheel ? 0x020Eu : 0x020Au;
        uint wParam = unchecked((uint)input.Delta << 16) | keys;
        int lParam = unchecked((Position.Y << 16) | (ushort)Position.X);
        return postMessage((nint)Window, message, wParam, lParam);
    }
}

public static class EdgeWheelPolicy
{
    public static PixelPoint ClampToMonitor(PixelPoint point, Rectangle monitor) =>
        new(Math.Clamp(point.X, monitor.Left, monitor.Right - 1),
            Math.Clamp(point.Y, monitor.Top, monitor.Bottom - 1));

    public static PixelPoint? InwardPoint(PixelPoint point, Rectangle monitor, uint dpi)
    {
        if (dpi == 0 || !monitor.Contains(point.X, point.Y)) return null;
        // 端の判定は物理ピクセル。内側の到達点だけをモニターの DPI に合わせる。
        long inset = (8L * dpi + 48) / 96;
        int x = InwardCoordinate(point.X, monitor.Left, monitor.Right - 1, inset);
        int y = InwardCoordinate(point.Y, monitor.Top, monitor.Bottom - 1, inset);
        return x == point.X && y == point.Y ? null : new PixelPoint(x, y);
    }

    private static int InwardCoordinate(int value, int first, int last, long inset)
    {
        long fromFirst = (long)value - first, fromLast = (long)last - value;
        if (fromFirst <= 2 && fromFirst <= fromLast)
            return (int)Math.Clamp(first + inset, value, last);
        if (fromLast <= 2)
            return (int)Math.Clamp(last - inset, first, value);
        return value;
    }

    public static bool CanProbe(bool maximized, Rectangle window, Rectangle monitor)
        => maximized || monitor.Width > 0 && monitor.Height > 0 && window.Contains(monitor);

    public static bool CanRedirect(bool eligibleWindow, long source, long target, bool sameRoot,
        bool chromium, bool ownProcess, bool elevatedTarget, bool administrator, bool chromiumEdge = false)
        => eligibleWindow && source != 0 && target != 0 && sameRoot &&
           (chromiumEdge ? chromium : !chromium && source != target) &&
           !ownProcess && (!elevatedTarget || administrator);
}
