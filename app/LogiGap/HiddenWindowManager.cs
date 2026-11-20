using System.Diagnostics;
using System.Text;
using LogiGap.Core;
using N = LogiGap.GestureNative;

namespace LogiGap;

/// <summary>UI thread owns notification icons and the recovery journal. Journal is committed
/// before SW_HIDE; entries are removed only after restoration or verified process destruction.</summary>
internal sealed class HiddenWindowManager : IDisposable
{
    private sealed record Entry(HiddenWindow Window, NotifyIcon? Tray, Icon? Icon);
    private readonly Dictionary<long, Entry> entries = [];
    private readonly HiddenWindowStore store = new(Path.Combine(AppContext.BaseDirectory, "state", "hidden_windows.json"));
    private readonly ILog log;
    private bool journalAvailable = true;
    public HiddenWindowManager(ILog log)
    {
        this.log = log;
        try
        {
            foreach (var window in store.Load()) entries[window.Handle] = new(window, null, null);
            RestoreAll();
        }
        catch (Exception ex) { journalAvailable = false; log.Write($"復元一覧を読めないため通知アイコン化を停止: {ex}"); }
    }
    private static bool SameWindow(HiddenWindow w)
    {
        if (!N.IsWindow((nint)w.Handle)) return false;
        N.GetWindowThreadProcessId((nint)w.Handle, out uint pid);
        if (pid != w.ProcessId) return false;
        using var process = Process.GetProcessById(w.ProcessId);
        return process.StartTime.ToUniversalTime().Ticks == w.ProcessStartedUtcTicks;
    }
    public void Hide(long handle)
    {
        if (!journalAvailable || entries.ContainsKey(handle)) return;
        nint window = (nint)handle;
        NotifyIcon? tray = null; Icon? icon = null;
        try
        {
            if (!N.IsWindowVisible(window)) return;
            N.GetWindowThreadProcessId(window, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            var title = new StringBuilder(1024); N.GetWindowTextW(window, title, title.Capacity);
            var saved = new HiddenWindow(handle, (int)pid, process.StartTime.ToUniversalTime().Ticks, title.ToString());
            nint source = 0;
            if (N.SendMessageTimeoutW(window, 0x7F, 2, 0, 2, 50, out nuint small) != 0) source = (nint)small;
            if (source == 0 && N.SendMessageTimeoutW(window, 0x7F, 1, 0, 2, 50, out nuint big) != 0) source = (nint)big;
            if (source == 0) source = N.GetClassLongPtrW(window, -34);
            if (source == 0) source = N.GetClassLongPtrW(window, -14);
            icon = source != 0 ? (Icon)Icon.FromHandle(source).Clone() : (Icon)SystemIcons.Application.Clone();
            var menu = new ContextMenuStrip();
            menu.Items.Add("元に戻す", null, (_, _) => Restore(handle, true));
            menu.Items.Add("閉じる", null, (_, _) =>
            {
                // 要確認: 閉じる際も先に再表示し、保存確認ダイアログを隠したままにしない。
                if (Restore(handle, false) && !N.PostMessageW(window, 0x112, 0xF060, 0)) log.Write("隠したウィンドウの閉じる操作が拒否されました");
            });
            tray = new NotifyIcon { Icon = icon, Text = saved.Title.Length > 63 ? saved.Title[..63] : saved.Title, ContextMenuStrip = menu };
            tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Restore(handle, true); };
            entries.Add(handle, new(saved, tray, icon));
            Persist(); // Failure here must never hide a window.
            tray.Visible = true;
            N.ShowWindow(window, 0);
            if (N.IsWindowVisible(window)) throw new InvalidOperationException("SW_HIDE が拒否されました（権限など）");
        }
        catch (Exception ex)
        {
            log.Write($"通知アイコン化失敗: {ex}");
            if (entries.ContainsKey(handle)) Restore(handle, false);
            else { tray?.ContextMenuStrip?.Dispose(); tray?.Dispose(); icon?.Dispose(); }
        }
    }
    private void Persist() => store.Save(entries.Values.Select(e => e.Window));
    public bool Restore(long handle, bool foreground)
    {
        if (!entries.TryGetValue(handle, out var entry)) return false;
        try
        {
            bool same = SameWindow(entry.Window);
            if (same)
            {
                N.ShowWindow((nint)handle, 5);
                if (!N.IsWindowVisible((nint)handle)) { log.Write("ウィンドウ復元が拒否されました。一覧を保持します"); return false; }
                if (foreground && !WindowsGestureServices.Foreground((nint)handle)) log.Write("復元したウィンドウの前面化が拒否されました");
            }
            entries.Remove(handle);
            entry.Tray?.ContextMenuStrip?.Dispose(); entry.Tray?.Dispose(); entry.Icon?.Dispose();
            Persist(); return same;
        }
        catch (ArgumentException) { RemoveDead(handle, entry); return false; }
        catch (Exception ex) { log.Write($"ウィンドウ復元失敗（一覧を保持）: {ex}"); return false; }
    }
    private void RemoveDead(long handle, Entry entry)
    {
        entries.Remove(handle); entry.Tray?.ContextMenuStrip?.Dispose(); entry.Tray?.Dispose(); entry.Icon?.Dispose();
        try { Persist(); } catch (Exception ex) { log.Write($"復元一覧保存失敗: {ex}"); }
    }
    public void RestoreAll() { foreach (long handle in entries.Keys.ToArray()) Restore(handle, false); }
    public void Prune()
    {
        foreach (var (handle, entry) in entries.ToArray())
        {
            try { if (!SameWindow(entry.Window)) RemoveDead(handle, entry); }
            catch (ArgumentException) { RemoveDead(handle, entry); }
            catch (Exception ex) { log.Write($"収納ウィンドウ監視: {ex.Message}"); }
        }
    }
    public void Dispose()
    {
        RestoreAll();
        foreach (var entry in entries.Values) { entry.Tray?.ContextMenuStrip?.Dispose(); entry.Tray?.Dispose(); entry.Icon?.Dispose(); }
    }
}
