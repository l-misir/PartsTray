// 設定画面の共通部品と読み込み・保存を管理します。
// 画面全体の配置や保存規則を変える場所です。
using System.Text.Json;
using LogiGap.Core;

namespace LogiGap;

internal sealed partial class SettingsWindow : Form
{
    private readonly string configPath;
    private readonly Func<IReadOnlyList<string>> deviceNames;
    private readonly Func<IReadOnlyList<BatteryDevice>> batteryDevices;
    private readonly Func<bool> readAutoStart;
    private readonly Action<bool> writeAutoStart;
    private readonly Action<string> openPath;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, Multiline = true };
    private readonly List<Action<AppSettings>> loaders = [];
    private readonly List<Func<AppSettings, AppSettings>> readers = [];
    private readonly List<Func<object?>> snapshots = [];
    private readonly Font hintFont = new("Yu Gothic UI", 8);
    private AppSettings original;
    private string baseline = "";
    private FileVersion? fileVersion;
    private bool loading, savedClose, refreshingAutoStart;
    private CheckBox autoStart = null!;
    private TabPage gapPage = null!;
    private bool supportedMouseSeen;

    // 内容も比較するので、同じ時刻の書き換えや削除・再作成も検出する。
    internal sealed record FileVersion(DateTime Stamp, string Bytes);
    internal static FileVersion? ReadVersion(string path)
    {
        try { return new(File.GetLastWriteTimeUtc(path), Convert.ToBase64String(File.ReadAllBytes(path))); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public SettingsWindow(string configPath, AppSettings settings, FileVersion? version,
        Func<IReadOnlyList<string>> deviceNames, Func<bool> readAutoStart, Action<bool> writeAutoStart, Action<string> openPath, bool supportedMouseSeen, Func<IReadOnlyList<BatteryDevice>> batteryDevices)
    {
        this.batteryDevices = batteryDevices;
        this.supportedMouseSeen = supportedMouseSeen;
        this.configPath = configPath; original = settings; fileVersion = version;
        this.deviceNames = deviceNames; this.readAutoStart = readAutoStart;
        this.writeAutoStart = writeAutoStart; this.openPath = openPath;
        Text = "LogiGap の設定"; ShowInTaskbar = true; Icon = SystemIcons.Application;
        Font = new Font("Yu Gothic UI", 9); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        // 11/20 実機（Claude）：Size を 96 DPI の値のまま入れると、125% でも 576×512 の物理ピクセルのままで小さく、
        // 1 画面に 4 項目ほどしか見えなかった。画面の拡大率に合わせて広げ、高さは作業領域の 9 割までにする。
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        int width = Math.Min(LogicalToDeviceUnits(640), area.Width), height = Math.Min(LogicalToDeviceUnits(760), area.Height * 9 / 10);
        Size = new Size(width, height); MinimumSize = new Size(LogicalToDeviceUnits(480), LogicalToDeviceUnits(400));
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(tabs, 0, 0);
        var footer = Flow(); footer.FlowDirection = FlowDirection.RightToLeft; footer.Dock = DockStyle.Fill;
        var apply = Button("適用", () => SaveSettings());
        var cancel = Button("キャンセル", Close);
        var ok = Button("OK", () => { if (SaveSettings()) { savedClose = true; Close(); } });
        footer.Controls.AddRange([apply, cancel, ok]); layout.Controls.Add(footer, 0, 1); Controls.Add(layout);
        AcceptButton = ok; CancelButton = cancel;
        BuildGap(); gapPage = tabs.TabPages[0];
        BuildGesture(); BuildWindows(); BuildIcons(); BuildOther();
        RefreshSupportedMouse(supportedMouseSeen);
        LoadControls(settings); baseline = Snapshot();
        Activated += (_, _) => RefreshAutoStart();
        FormClosing += (_, e) =>
        {
            gestureGrid.EndEdit();
            if (savedClose || Snapshot() == baseline) return;
            var answer = Ask(this, "変更を保存しますか？", ("保存", DialogResult.Yes),
                ("保存しない", DialogResult.No), ("キャンセル", DialogResult.Cancel));
            e.Cancel = answer == DialogResult.Cancel || (answer == DialogResult.Yes && !SaveSettings());
        };
    }

    public void RefreshSupportedMouse(bool seen)
    {
        supportedMouseSeen |= seen;
        bool show = original.GapFiller.Enabled || supportedMouseSeen;
        if (show && !tabs.TabPages.Contains(gapPage)) tabs.TabPages.Insert(0, gapPage);
        else if (!show && tabs.TabPages.Contains(gapPage)) tabs.TabPages.Remove(gapPage);
    }

    private void LoadControls(AppSettings settings)
    {
        // 隠れたタブは「既定に戻す」でも値を保持する。
        if (!tabs.TabPages.Contains(gapPage)) settings = settings with { GapFiller = original.GapFiller };
        loading = true;
        try { foreach (var load in loaders) load(settings); }
        finally { loading = false; }
    }
    private string Snapshot() => JsonSerializer.Serialize(snapshots.Select(s => s()).ToArray());

    private bool SaveSettings()
    {
        try
        {
            if (!ValidateChildren() || !gestureGrid.EndEdit()) return false;
            var draft = original;
            foreach (var read in readers) draft = read(draft);
            var value = SettingsText.MergeEditable(original, draft, editGap: tabs.TabPages.Contains(gapPage));
            // 読み取り失敗時は上書きを行わず、エラーを表示する。
            if (ReadVersion(configPath) != fileVersion && Ask(this, "ほかで変更されています。上書きしますか？",
                ("上書きする", DialogResult.Yes), ("キャンセル", DialogResult.Cancel)) != DialogResult.Yes) return false;
            AppSettings.Save(configPath, value);
            fileVersion = ReadVersion(configPath); original = value;
            RefreshSupportedMouse(supportedMouseSeen);
            LoadControls(value); baseline = Snapshot();
            return true; // 既存の 1 秒周期の Reload に任せる。
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "設定を保存できませんでした。\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private TableLayoutPanel Page(string title)
    {
        var page = new TabPage(title) { AutoScroll = true, Padding = new Padding(6) };
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(panel); tabs.TabPages.Add(page); return panel;
    }

    // title が null のときは見出しを出さない（チェックは、チェックの文字そのものを見出しにする）。
    private void Field(TableLayoutPanel page, string? title, Control editor, string hint)
    {
        var group = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Margin = new Padding(3, 5, 3, 8) };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var label = title == null ? null : new Label { Text = title, AutoSize = true, Dock = DockStyle.Top };
        var help = new Label { Text = hint, AutoSize = true, ForeColor = SystemColors.GrayText, Font = hintFont, Dock = DockStyle.Top };
        if (label != null) group.Controls.Add(label);
        group.Controls.Add(editor); group.Controls.Add(help);
        // 狭い幅では説明を折り返し、高さを自動計算する。
        group.SizeChanged += (_, _) =>
        {
            int width = Math.Max(1, group.ClientSize.Width - group.Padding.Horizontal - 6);
            if (label != null) label.MaximumSize = new Size(width, 0);
            help.MaximumSize = new Size(width, 0);
            if (editor is CheckBox check) check.MaximumSize = new Size(width, 0);
            if (editor is Label message) message.MaximumSize = new Size(width, 0);
        };
        page.Controls.Add(group);
    }
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        button.Click += (_, _) => action(); return button;
    }
    private void Bind<T>(Control control, Func<AppSettings, T> get, Action<T> load, Func<T> read,
        Func<AppSettings, T, AppSettings> set, Func<object?> snapshot)
    {
        loaders.Add(s => load(get(s))); snapshots.Add(snapshot);
        readers.Add(s =>
        {
            try { var value = read(); control.BackColor = SystemColors.Window; return set(s, value); }
            catch (InvalidDataException) { ShowError(control); throw; }
        });
    }
    private void ShowError(Control control)
    {
        control.BackColor = Color.MistyRose;
        for (Control? c = control; c != null; c = c.Parent)
            if (c is TabPage page) { tabs.SelectedTab = page; page.ScrollControlIntoView(control); break; }
        control.Focus();
    }
    private CheckBox Check(TableLayoutPanel page, string title, string hint, Func<AppSettings, bool> get, Func<AppSettings, bool, AppSettings> set)
    {
        // 11/20 Claude：見出し＋「有効にする」の 2 行だと縦に長くなるので、見出しをチェックの文字にして 1 行にした。
        var control = new CheckBox { Text = title, AutoSize = true };
        Field(page, null, control, hint);
        Bind(control, get, v => control.Checked = v, () => control.Checked, set, () => control.Checked);
        return control;
    }
    private NumericUpDown Number(TableLayoutPanel page, string title, string hint, decimal min, decimal max,
        Func<AppSettings, decimal> get, Func<AppSettings, decimal, AppSettings> set, int decimals = 0, decimal step = 1)
    {
        var control = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step,
            Width = 130, ReadOnly = step == 50 };
        ScrollPageInstead(control);
        Field(page, title, control, hint);
        // 要確認: DPI は矢印操作だけにして 50 刻みを保証し、ほかの数値の直接入力は表示桁数に四捨五入する。
        Bind(control, get, v => control.Value = v, () =>
        {
            control.Value = decimal.Round(control.Value, decimals, MidpointRounding.AwayFromZero);
            return control.Value;
        }, set, () => control.Text);
        return control;
    }
    // 11/20 ユーザー：「〇〇 ▼」の上でホイールを回すと、ページをスクロールしたつもりで設定が変わってしまう。
    // 選択肢と数値の欄では、ホイールで値を変えずにページをスクロールする（一覧を開いているときは今までどおり一覧を動かす）。
    // ページ（TabPage）の動きに合わせ、1 目盛りで Delta（120）ドット動かす。
    internal static void ScrollPageInstead(Control control) => control.MouseWheel += (_, e) =>
    {
        if (control is ComboBox { DroppedDown: true }) return;
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        for (Control? c = control.Parent; c != null; c = c.Parent)
            if (c is ScrollableControl { AutoScroll: true } page)
            {
                var at = page.AutoScrollPosition;   // 位置は負の値で返る
                page.AutoScrollPosition = new Point(-at.X, Math.Max(0, -at.Y - e.Delta));
                return;
            }
    };
    private sealed record Choice(string Value, string Label) { public override string ToString() => Label; }
    private ComboBox ChoiceBox(TableLayoutPanel page, string title, string hint, (string Value, string Label)[] choices,
        Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set)
    {
        var control = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        control.Items.AddRange(choices.Select(c => new Choice(c.Value, c.Label)).ToArray());
        ScrollPageInstead(control);
        Field(page, title, control, hint);
        Bind(control, get, v => control.SelectedIndex = Array.FindIndex(choices, c => c.Value == v),
            () => (control.SelectedItem as Choice)?.Value ?? throw new InvalidDataException("選択肢を選んでください。"),
            set, () => control.SelectedIndex);
        return control;
    }
    private void ColorField(TableLayoutPanel page, string title, string hint, Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set)
    {
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
                string color = SettingsText.ColorText(text.Text);
                swatch.BackColor = ColorTranslator.FromHtml(color);
                swatch.ForeColor = swatch.BackColor.GetBrightness() < 0.5f ? Color.White : Color.Black;
                text.BackColor = SystemColors.Window;
            }
            catch (InvalidDataException) { text.BackColor = Color.MistyRose; }
        };
        row.Controls.AddRange([swatch, text]); Field(page, title, row, hint);
        Bind(text, get, v => text.Text = v, () => SettingsText.ColorText(text.Text), set, () => text.Text);
    }
    private void Processes(TableLayoutPanel page, string title, string hint, Func<AppSettings, string[]> get, Func<AppSettings, string[], AppSettings> set)
    {
        var text = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 80, Dock = DockStyle.Top };
        Field(page, title, text, hint);
        Bind(text, get, v => text.Lines = v, () => SettingsText.ProcessLines(text.Text), set, () => text.Text);
    }

    internal static DialogResult Ask(IWin32Window? owner, string message, params (string Text, DialogResult Result)[] choices)
    {
        using var dialog = new Form { Text = "LogiGap の設定", Font = new Font("Yu Gothic UI", 9),
            AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimizeBox = false, MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            ShowInTaskbar = false, Icon = SystemIcons.Application };
        var panel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
        panel.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(3, 3, 3, 12) });
        var buttons = Flow();
        foreach (var choice in choices)
        {
            var button = Button(choice.Text, () => { dialog.DialogResult = choice.Result; dialog.Close(); });
            buttons.Controls.Add(button);
            if (choice.Result == DialogResult.Cancel) dialog.CancelButton = button;
        }
        panel.Controls.Add(buttons); dialog.Controls.Add(panel);
        return dialog.ShowDialog(owner);
    }

    public void RefreshAutoStart()
    {
        if (IsDisposed || autoStart == null) return;
        refreshingAutoStart = true;
        try { autoStart.Checked = readAutoStart(); autoStart.Enabled = true; }
        catch (Exception) { autoStart.Enabled = false; }
        finally { refreshingAutoStart = false; }
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { gapPage.Dispose(); hintFont.Dispose(); }
    }
}
