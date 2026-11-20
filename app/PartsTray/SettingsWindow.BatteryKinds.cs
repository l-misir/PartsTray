// 設定画面に、電池を読めた機器の名前と種類の選択表を作ります。
// 選択肢や「自動」の表示を変える場所です。未検出の設定は保存時も保持します。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private void BuildBatteryKinds(TableLayoutPanel page)
    {
        var table = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var editors = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        string[] values = ["mouse", "keyboard", "headphones", "earbuds", "gamepad", "other"];
        Field(page, "機器の種類", table, "自動判定と違うときは種類を選んでください。同じ名前の機器すべてに適用します。");
        loaders.Add(s =>
        {
            foreach (Control c in table.Controls.Cast<Control>().ToArray()) c.Dispose();
            table.Controls.Clear(); table.RowStyles.Clear(); table.RowCount = 0; editors.Clear();
            // 要確認: 同名機器は名前で設定する仕様のため 1 行にまとめ、自動の表示は ID 順の先頭を使う。
            foreach (var group in batteryDevices().Where(d => d.Reading != null).GroupBy(d => d.Name, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var device = group.OrderBy(d => d.Id, StringComparer.Ordinal).First();
                int row = table.RowCount++;
                table.Controls.Add(new Label { Text = group.Key, AutoSize = true, Dock = DockStyle.Top }, 0, row);
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
                ScrollPageInstead(combo);   // ホイールで種類が変わらないように（SettingsWindow.cs）
                combo.Items.Add(new Choice("", $"自動（いまは{BatteryKinds.Label(device.Kind)}）"));
                foreach (string value in values)
                {
                    BatteryKinds.TryParse(value, out var kind);
                    combo.Items.Add(new Choice(value, BatteryKinds.Label(kind)));
                }
                string selected = s.Battery.Kinds.GetValueOrDefault(group.Key, "");
                combo.SelectedIndex = combo.Items.Cast<Choice>().ToList().FindIndex(c => c.Value == selected);
                table.Controls.Add(combo, 1, row); editors.Add(group.Key, combo);
            }
        });
        snapshots.Add(() => editors.Select(e => new[] { e.Key, (e.Value.SelectedItem as Choice)?.Value }).ToArray());
        readers.Add(s =>
        {
            var kinds = new Dictionary<string, string>(s.Battery.Kinds, StringComparer.Ordinal);
            foreach (var (name, combo) in editors)
            {
                string value = (combo.SelectedItem as Choice)?.Value ?? "";
                if (value.Length == 0) kinds.Remove(name); else kinds[name] = value;
            }
            return s with { Battery = s.Battery with { Kinds = kinds } };
        });
    }
}
