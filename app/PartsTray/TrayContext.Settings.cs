// トレイの入口と設定画面の表示をつなぎます。
// 左クリック・右クリックの移動先や設定の読み込みを変えるときに開きます。
using System.Diagnostics;
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class TrayContext
{
    private void BindSettingsIcon(NotifyIcon target, SettingsIconKind kind)
    {
        long doubleClickAt = long.MinValue / 2;
        SettingsDestination Destination() => SettingsNavigation.Resolve(kind, target.Tag as string);
        // メニュー表示より前の押下で覚える。共有メニューの SourceControl は NotifyIcon を返さない。
        target.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) { clickTimer.Stop(); menuDestination = Destination(); }
        };
        target.MouseDoubleClick += (_, _) => { doubleClickAt = Environment.TickCount64; clickTimer.Stop(); };
        target.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && Environment.TickCount64 - doubleClickAt > SystemInformation.DoubleClickTime)
            { clickDestination = Destination(); clickTimer.Stop(); clickTimer.Start(); }
            else if (e.Button == MouseButtons.Middle && kind == SettingsIconKind.Temperature) gestures.TogglePause();
        };
    }

    private void OpenSettings() => OpenSettings(new SettingsDestination());

    private void OpenSettings(SettingsDestination destination)
    {
        if (disposing || openingSettings) return;
        if (settingsWindow is { IsDisposed: false } existing)
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Show(); existing.Navigate(destination); existing.BringToFront(); existing.Activate(); return;
        }
        var timing = Stopwatch.StartNew();
        openingSettings = true;
        try
        {
            AppSettings settings;
            SettingsWindow.FileVersion? version = null;
            try
            {
                var before = SettingsWindow.ReadVersion(configPath);
                settings = AppSettings.Load(configPath);
                version = SettingsWindow.ReadVersion(configPath);
                if (before != null && before != version) throw new IOException("読み込み中に設定ファイルが変更されました。");
            }
            catch (Exception ex)
            {
                if (SettingsWindow.Ask(null, "設定ファイルを読み込めませんでした。\n" + ex.Message,
                    ("既定の設定で開く", DialogResult.Yes), ("やめる", DialogResult.Cancel)) != DialogResult.Yes) return;
                settings = new AppSettings();
                // 要確認: 読み込みに失敗したときは、保存時に改めて外部変更の確認を行う。
                version = null;
            }
            long readMs = timing.ElapsedMilliseconds;
            var window = new SettingsWindow(configPath, settings, version, battery.GetDeviceNames, ReadAutoStart, WriteAutoStart, Open, gap.SupportedMouseSeen, battery.GetDevices, log);
            long createdMs = timing.ElapsedMilliseconds - readMs;
            settingsWindow = window;
            window.AdministratorEnabled += () => Post(OfferAdministratorRestart);
            window.FormClosed += (_, _) => { if (ReferenceEquals(settingsWindow, window)) settingsWindow = null; window.Dispose(); };
            window.Navigate(destination);
            window.Show();
            log.Write($"設定画面：設定読込 {readMs}ms・作成 {createdMs}ms・表示まで {timing.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            log.Write($"設定ウィンドウ: {ex}");
            MessageBox.Show("設定ウィンドウを開けませんでした。\n" + ex.Message, "PartsTray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { openingSettings = false; }
    }
}
