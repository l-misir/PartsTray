// タスクバーの明暗を 5 秒ごとに読み、モニターと電池へ同時に通知します。
// レジストリの読み方・失敗時の扱い・手動指定を変える場所です。
using Microsoft.Win32;

namespace LogiGap;

internal sealed class TaskbarTheme : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 5000 };
    private string setting;
    private bool systemLight;
    public bool IsLight { get; private set; }
    public event Action? Changed;

    public TaskbarTheme(string setting)
    {
        this.setting = setting;
        Read(); timer.Tick += (_, _) => Read(); timer.Start();
    }

    public void SettingsChanged(string value) { setting = value; Read(); }

    private void Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("SystemUsesLightTheme") is int value && value is 0 or 1) systemLight = value == 1;
        }
        // 要確認: 読めない・値が不正な場合は直前のシステム明暗を維持する（初期値は暗い）。手動指定は優先する。
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        bool next = setting switch { "light" => true, "dark" => false, _ => systemLight };
        if (next == IsLight) return;
        IsLight = next; Changed?.Invoke();
    }

    public void Dispose() { timer.Stop(); timer.Dispose(); }
}
