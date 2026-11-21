// ジェスチャー設定のチェック項目と動作一覧を表示・保存します。
// ジェスチャー設定画面の項目や説明を変えるときに開きます。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private DataGridView gestureGrid = null!;
    private static readonly Choice[] GestureCommands = [new("Back", "戻る（Back）"), new("Forward", "進む（Forward）"),
        new("Refresh", "更新（Refresh）"), new("Close", "閉じる（Close）"), new("Minimize", "最小化（Minimize）")];

    private void BuildGesture()
    {
        var p = Page("ジェスチャー");
        Check(p, "ジェスチャーを使う", "右ボタンを押して形を描き、離すと登録した動作を実行します。", s => s.Gesture.Enabled,
            (s, v) => s with { Gesture = s.Gesture with { Enabled = v } });
        Check(p, "右ボタン＋ホイールで切り替える", "右ボタンを押したままホイールを回すと、同じモニターで最大化しているアプリを切り替えます（下・右で次、上・左で前）。ジェスチャーがオンのときだけ働きます。向きと 1 つ進む量は『ウィンドウ・ホイール』の設定を使います。", s => s.Gesture.RightWheelSwitch,
            (s, v) => s with { Gesture = s.Gesture with { RightWheelSwitch = v } });
        Number(p, "動いたと見なす距離（ピクセル）", "この距離を動かすと、形の向きとして認識します。", 1, 1000, s => s.Gesture.StrokeDistancePx,
            (s, v) => s with { Gesture = s.Gesture with { StrokeDistancePx = (int)v } });
        Number(p, "始めるまでの待ち時間（ミリ秒）", "右ボタンを押してから、この時間までに動かさなければ普通の右ドラッグになります。右ボタン＋ホイールが有効なときは、ホイールを待つため解放まで押下を保持します。", 1, 2000, s => s.Gesture.StartTimeoutMs,
            (s, v) => s with { Gesture = s.Gesture with { StartTimeoutMs = (int)v } });
        Number(p, "説明を出すまでの時間（ミリ秒）", "ジェスチャー中にこの時間静止すると、形と動作の説明を表示します。", 1, 60000, s => s.Gesture.TooltipDelayMs,
            (s, v) => s with { Gesture = s.Gesture with { TooltipDelayMs = (int)v } });
        Check(p, "押し続けて連打", "右ボタンを押したまま左クリックするたびに実行します。その後の右ボタン解放では追加実行しません。", s => s.Gesture.RapidFire,
            (s, v) => s with { Gesture = s.Gesture with { RapidFire = v } });
        Check(p, "線を描く", "認識した形をマウスの軌跡として画面に描きます。", s => s.Gesture.Trail.Enabled,
            (s, v) => s with { Gesture = s.Gesture with { Trail = s.Gesture.Trail with { Enabled = v } } });
        Number(p, "線の太さ（ピクセル）", "軌跡の太さを 1〜100 で指定します。", 1, 100, s => (decimal)s.Gesture.Trail.WidthPx,
            (s, v) => s with { Gesture = s.Gesture with { Trail = s.Gesture.Trail with { WidthPx = (float)v } } }, 1, 0.1m);
        ColorField(p, "線の色（認識できた）", "登録した形と一致しているときの線の色です。", s => s.Gesture.Trail.ValidColor,
            (s, v) => s with { Gesture = s.Gesture with { Trail = s.Gesture.Trail with { ValidColor = v } } });
        ColorField(p, "線の色（認識できない）", "登録した形と一致していないときの線の色です。", s => s.Gesture.Trail.InvalidColor,
            (s, v) => s with { Gesture = s.Gesture with { Trail = s.Gesture.Trail with { InvalidColor = v } } });
        BuildGestureGrid(p);
        Processes(p, "ブラウザーとして扱うアプリ", "chrome.exe のように 1 行に 1 つ入力します。「閉じる」でタブを閉じます。", s => s.Gesture.BrowserProcesses,
            (s, v) => s with { Gesture = s.Gesture with { BrowserProcesses = v } });
        Processes(p, "ジェスチャーを使わないアプリ", "アプリの実行ファイル名を 1 行に 1 つ入力します。", s => s.Gesture.ExcludedProcesses,
            (s, v) => s with { Gesture = s.Gesture with { ExcludedProcesses = v } });
        Check(p, "エクスプローラーで選択中は飛ばす", "選択済みファイルの文字・アイコン上ではジェスチャーを始めず、右ドラッグのコピー・移動を使えます。", s => s.Gesture.ExplorerSkipSelectedItems,
            (s, v) => s with { Gesture = s.Gesture with { ExplorerSkipSelectedItems = v } });
    }

    private void BuildGestureGrid(TableLayoutPanel page)
    {
        gestureGrid = new DataGridView { Dock = DockStyle.Top, Height = 210, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = true };
        gestureGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pattern", HeaderText = "形", FillWeight = 55 });
        gestureGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Command", HeaderText = "動作", FillWeight = 45,
            DataSource = GestureCommands, DisplayMember = nameof(Choice.Label), ValueMember = nameof(Choice.Value) });
        gestureGrid.DefaultValuesNeeded += (_, e) => e.Row.Cells[1].Value = "Back";
        // 「動作」の欄を編集中も、ホイールで動作が変わらないようにする（編集用の欄は使い回されるので 1 回だけ付ける）。
        var wheelHooked = new HashSet<Control>();
        gestureGrid.EditingControlShowing += (_, e) => { if (e.Control is ComboBox box && wheelHooked.Add(box)) ScrollPageInstead(box); };
        gestureGrid.DataError += (_, e) => { e.ThrowException = false; if (e.RowIndex >= 0) gestureGrid.Rows[e.RowIndex].ErrorText = "動作を選んでください。"; };
        gestureGrid.CellEndEdit += (_, e) =>
        {
            if (e.ColumnIndex == 0 && e.RowIndex >= 0)
            {
                var cell = gestureGrid.Rows[e.RowIndex].Cells[0];
                try { cell.Value = SettingsText.PatternArrows(cell.Value?.ToString() ?? ""); }
                catch (InvalidDataException) { }
            }
            MarkGestureErrors();
        };
        gestureGrid.RowsRemoved += (_, _) => { if (!loading) MarkGestureErrors(); };
        var group = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); group.Controls.Add(gestureGrid);
        // 11/20 実機（Claude）：折り返しありだと、表の中で高さを多めに見積もられ、ボタンの下に大きな空白ができた。1 列に並べる。
        var buttons = Flow(); buttons.WrapContents = false;
        foreach (string arrow in new[] { "←", "→", "↑", "↓" }) buttons.Controls.Add(Button(arrow, () => EditPattern(arrow)));
        buttons.Controls.Add(Button("1 つ消す", () => EditPattern(null)));
        buttons.Controls.Add(Button("行を追加", () =>
        {
            gestureGrid.EndEdit();
            int index = gestureGrid.Rows.Add("", "Back"); gestureGrid.CurrentCell = gestureGrid.Rows[index].Cells[0];
        }));
        buttons.Controls.Add(Button("行を削除", () =>
        {
            gestureGrid.EndEdit();
            if (gestureGrid.CurrentRow is { IsNewRow: false } row) gestureGrid.Rows.Remove(row);
        }));
        group.Controls.Add(buttons);
        Field(page, "ジェスチャーの一覧", group, "形は矢印または LRUD で入力できます。赤い行を直してから保存してください（256 行まで）。");
        loaders.Add(s =>
        {
            gestureGrid.Rows.Clear();
            foreach (var (pattern, command) in s.Gesture.Gestures) gestureGrid.Rows.Add(SettingsText.PatternArrows(pattern), command);
        });
        snapshots.Add(() => GestureRows().Select(r => new[] { r.Pattern, r.Command }).ToArray());
        readers.Add(s =>
        {
            MarkGestureErrors();
            try { return s with { Gesture = s.Gesture with { Gestures = SettingsText.ParseGestures(GestureRows()) } }; }
            catch (InvalidDataException) { ShowError(gestureGrid); throw; }
        });
    }
    private List<(string Pattern, string Command)> GestureRows() => gestureGrid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
        .Select(r => (r.Cells[0].Value?.ToString() ?? "", r.Cells[1].Value?.ToString() ?? "")).ToList();
    private void MarkGestureErrors()
    {
        var errors = SettingsText.GestureErrors(GestureRows());
        int index = 0;
        foreach (DataGridViewRow row in gestureGrid.Rows)
        {
            if (row.IsNewRow) continue;
            row.ErrorText = errors.GetValueOrDefault(index++, "");
            row.DefaultCellStyle.BackColor = row.ErrorText.Length == 0 ? SystemColors.Window : Color.MistyRose;
            row.DefaultCellStyle.SelectionBackColor = row.ErrorText.Length == 0 ? SystemColors.Highlight : Color.Firebrick;
        }
    }
    private void EditPattern(string? arrow)
    {
        gestureGrid.EndEdit();
        var row = gestureGrid.CurrentRow;
        if (row == null || row.IsNewRow)
        {
            if (arrow == null) return;
            row = gestureGrid.Rows[gestureGrid.Rows.Add("", "Back")];
        }
        string text = row.Cells[0].Value?.ToString() ?? "";
        text = arrow == null ? (text.Length == 0 ? "" : text[..^1]) : text + arrow;
        try { text = SettingsText.PatternArrows(text); } catch (InvalidDataException) { }
        row.Cells[0].Value = text; gestureGrid.CurrentCell = row.Cells[0]; MarkGestureErrors();
    }
}
