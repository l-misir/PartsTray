// 設定画面でアイコンに対応する欄へ移動します。
// タブ選択・スクロール・機器の種類へのフォーカスを変えるときに開きます。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private readonly Dictionary<string, Control> navigationFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComboBox> batteryKindEditors = new(StringComparer.Ordinal);
    private SettingsDestination destination = new();

    public void Navigate(SettingsDestination target)
    {
        destination = target;
        if (target.Tab == null) return;
        var page = tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == target.Tab);
        if (page == null) return;
        tabs.SelectedTab = page;
        FocusDestination();
    }

    private void FocusDestination()
    {
        Control? control = null;
        if (destination.DeviceName is { } name && batteryKindEditors.TryGetValue(name, out var combo)) control = combo;
        if (control == null && destination.Field is { } field) navigationFields.TryGetValue(field, out control);
        if (control == null || tabs.SelectedTab is not { } page || !page.Contains(control)) return;
        page.ScrollControlIntoView(control);
        control.Select();
    }
}
