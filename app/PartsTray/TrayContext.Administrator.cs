// トレイと設定画面から自動起動・管理者再起動を共通の処理へ渡します。
// 設定保存後の再起動確認や、キャンセル通知を変えるときに開きます。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class TrayContext
{
    private readonly SingleInstance instance;
    private readonly AutoStartManager autoStartManager;
    private AppSettings currentSettings = new();

    private bool ReadAutoStart() => autoStartManager.Read();
    private void WriteAutoStart(bool enabled)
    {
        bool administrator = AppSettings.Load(configPath).RunAsAdministrator;
        autoStartManager.Write(enabled, administrator);
        settingsWindow?.RefreshAutoStart();
    }
    private void OfferAdministratorRestart()
    {
        Reload();
        if (disposing || ProcessElevation.IsAdministrator || !currentSettings.RunAsAdministrator) return;
        if (SettingsWindow.Ask(settingsWindow, "今すぐ管理者として起動し直しますか？",
            ("起動し直す", DialogResult.Yes), ("あとで", DialogResult.Cancel)) != DialogResult.Yes) return;
        if (AdministratorRestart.TryRestart(instance, log, SwitchNotice)) ExitThread();
    }
}
