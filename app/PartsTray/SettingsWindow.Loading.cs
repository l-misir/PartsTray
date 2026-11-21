// 設定画面を表示してから一覧と自動起動を非同期で読みます。
// 読み込み中の表示・時間の記録・閉じた後の扱いを変えるときに開きます。
using System.Diagnostics;
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private readonly ILog settingsLog;
    private readonly Label deviceLoading = new() { AutoSize = true, Text = "読み込み中…" };
    private readonly Label autoStartLoading = new() { AutoSize = true, Text = "読み込み中…" };
    private IReadOnlyList<string> cachedDeviceNames = [];
    private IReadOnlyList<BatteryDevice> cachedBatteryDevices = [];
    private Action? refreshDeviceNames, refreshBatteryKinds;
    private bool deferredStarted, autoStartReading, autoStartReadAgain, deviceListReady;

    private async void StartDeferredLoading()
    {
        if (IsDisposed || deferredStarted) return;
        deferredStarted = true;
        FocusDestination();
        RefreshAutoStart();
        var timing = Stopwatch.StartNew();
        try
        {
            // BatteryModule の辞書は UI 所有。OS を照会せず取得済みの値だけをコピーする。
            var names = readDeviceNames().ToArray();
            var devices = readBatteryDevices().ToArray();
            var prepared = await Task.Run(() => (
                Names: names.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                Devices: devices.OrderBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Id, StringComparer.Ordinal).ToArray()));
            if (IsDisposed || Disposing) return;
            cachedDeviceNames = prepared.Names; cachedBatteryDevices = prepared.Devices; deviceListReady = true;
            refreshDeviceNames?.Invoke(); refreshBatteryKinds?.Invoke();
            deviceLoading.Text = devices.Length == 0 ? "取得済みの機器はありません" : "読み込み完了";
            // 読み込み待ちに別のタブを選んだ場合は戻さない。
            if (destination.DeviceName != null && tabs.SelectedTab?.Text == destination.Tab) FocusDestination();
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing) return;
            deviceLoading.Text = "機器の一覧を読み込めませんでした";
            refreshBatteryKinds?.Invoke();
            settingsLog.Write($"設定画面：電池の一覧の読込失敗 {ex.Message}");
        }
        finally { settingsLog.Write($"設定画面：電池の一覧 {timing.ElapsedMilliseconds}ms"); }
    }

    public async void RefreshAutoStart()
    {
        if (IsDisposed || Disposing || !deferredStarted) return;
        if (autoStartReading) { autoStartReadAgain = true; return; }
        autoStartReading = true; autoStart.Enabled = false; autoStartLoading.Text = "読み込み中…";
        var timing = Stopwatch.StartNew();
        try
        {
            bool enabled = await Task.Run(readAutoStart);
            if (IsDisposed || Disposing) return;
            refreshingAutoStart = true;
            try { autoStart.Checked = enabled; }
            finally { refreshingAutoStart = false; }
            autoStart.Enabled = true; autoStartLoading.Text = "";
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing) return;
            autoStartLoading.Text = "自動起動の状態を読み込めませんでした";
            settingsLog.Write($"設定画面：自動起動の読込失敗 {ex.Message}");
        }
        finally
        {
            autoStartReading = false;
            settingsLog.Write($"設定画面：自動起動の照会 {timing.ElapsedMilliseconds}ms");
            if (autoStartReadAgain) { autoStartReadAgain = false; RefreshAutoStart(); }
        }
    }
}
