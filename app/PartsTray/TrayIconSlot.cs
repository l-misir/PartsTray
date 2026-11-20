// 通知アイコンの画像と HICON の寿命を共通管理します。
// 差し替え後に古いハンドルを解放する順序を維持してください。
using System.Runtime.InteropServices;
namespace PartsTray;

internal sealed class TrayIconSlot<TKey> : IDisposable where TKey : class
{
    private readonly NotifyIcon target;
    private readonly TrayOrder? order;
    private nint handle;
    private Icon? wrapper;
    public TrayIconSlot(NotifyIcon target) => this.target = target;
    public TrayIconSlot(ContextMenuStrip menu, TrayOrder order, int rank)
    {
        target = new() { ContextMenuStrip = menu }; this.order = order;
        order.Register(target, rank);
    }
    public TKey? Key { get; set; }
    public NotifyIcon Target => target;
    public bool[,]? BigMask { get; set; }
    public void SetTooltip(string text) { if (target.Text != text) target.Text = text; }
    public void Show(bool show) { if (show && target.Icon is null) return; order!.Want(target, show); }
    public void Replace(Bitmap bitmap)
    {
        nint nextHandle = bitmap.GetHicon();
        Icon? next = null;
        try { next = Icon.FromHandle(nextHandle); target.Icon = next; }
        catch { next?.Dispose(); TrayIconNative.DestroyIcon(nextHandle); throw; }
        Release(); handle = nextHandle; wrapper = next; Key = null; BigMask = null;
    }
    public void Reset(Icon? replacement) { target.Icon = replacement; Release(); Key = null; BigMask = null; }
    private void Release()
    {
        wrapper?.Dispose(); wrapper = null;
        if (handle != 0) { TrayIconNative.DestroyIcon(handle); handle = 0; }
    }
    public void Dispose()
    {
        target.Visible = false; target.Icon = null; target.ContextMenuStrip = null;
        target.Dispose(); Release();
    }
}

internal static class TrayIconNative
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyIcon(nint icon);
}
