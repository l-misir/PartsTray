// 数字と背景の色を入力・選択する共通部品です。
// 色の検査・見本・透明に戻す操作を変えるときに開きます。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private void ColorField(TableLayoutPanel page, string title, string hint, Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set, bool transparent = false)
    {
        string ReadColor(string value) => transparent ? SettingsText.BackgroundColorText(value) : SettingsText.ColorText(value);
        var row = Flow(); var text = new TextBox { Width = 100, CharacterCasing = CharacterCasing.Upper };
        var swatch = Button("色を選ぶ", () =>
        {
            using var dialog = new ColorDialog { FullOpen = true };
            if (MonitorColor.TryParse(text.Text, out var c)) dialog.Color = Color.FromArgb(c.R, c.G, c.B);
            if (dialog.ShowDialog(this) == DialogResult.OK)
                text.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        });
        text.TextChanged += (_, _) =>
        {
            try
            {
                string color = ReadColor(text.Text);
                swatch.BackColor = color == "" ? SystemColors.Control : ColorTranslator.FromHtml(color);
                swatch.ForeColor = swatch.BackColor.GetBrightness() < 0.5f ? Color.White : Color.Black;
                text.BackColor = SystemColors.Window;
            }
            catch (InvalidDataException) { text.BackColor = Color.MistyRose; }
        };
        row.Controls.AddRange([swatch, text]);
        if (transparent) row.Controls.Add(Button("透明に戻す", () => text.Clear()));
        Field(page, title, row, hint);
        Bind(text, get, v => text.Text = v, () => ReadColor(text.Text), set, () => text.Text);
    }
}
