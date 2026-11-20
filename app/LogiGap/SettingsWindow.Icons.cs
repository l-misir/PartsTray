// 通知領域のモニター・電池・その他の設定欄を作ります。
// アイコンの設定項目や説明を変える場所です。
using LogiGap.Core;

namespace LogiGap;

internal sealed partial class SettingsWindow
{
    private void BuildIcons()
    {
        var p = Page("通知領域のアイコン");
        ChoiceBox(p, "タスクバーの明るさ", "自動で合わないときは、明るい・暗いを指定してください。電池のアイコンにも適用します。",
            [("auto", "自動"), ("dark", "暗い"), ("light", "明るい")], s => s.Monitor.TaskbarTheme,
            (s, v) => s with { Monitor = s.Monitor with { TaskbarTheme = v } });
        Check(p, "CPU・温度・メモリのアイコンを出す", "オウムと使用率・温度の表示をまとめて切り替えます。", s => s.Monitor.Enabled,
            (s, v) => s with { Monitor = s.Monitor with { Enabled = v } });
        Number(p, "更新の間隔（ミリ秒）", "CPU・メモリなどの数字を読み直す間隔です。温度の読み取りは 1 秒ごとです。", 100, 5000, s => s.Monitor.SampleIntervalMs,
            (s, v) => s with { Monitor = s.Monitor with { SampleIntervalMs = (int)v } });
        ChoiceBox(p, "数字の形", "通知領域の数字に共通する文字の形です。", [("thin", "Core Temp 風（thin）"), ("wide", "点の文字を伸ばす（wide）")],
            s => s.Monitor.DigitStyle, (s, v) => s with { Monitor = s.Monitor with { DigitStyle = v } });
        Check(p, "オウムを出す", "CPU の使用率に合わせて動くオウムを表示します。", s => s.Monitor.Parrot.Enabled,
            (s, v) => s with { Monitor = s.Monitor with { Parrot = s.Monitor.Parrot with { Enabled = v } } });
        ChoiceBox(p, "オウムに CPU 使用率の数字を添える", "オウムと一緒に使用率を表示する位置を選びます。",
            [("none", "添えない（none）"), ("bottom", "下（bottom）"), ("top", "上（top）")], s => s.Monitor.Parrot.CpuText,
            (s, v) => s with { Monitor = s.Monitor with { Parrot = s.Monitor.Parrot with { CpuText = v } } });
        Check(p, "オウムを縦に伸ばす", "数字を添えないとき、絵を縦に伸ばして枠いっぱいに表示します。", s => s.Monitor.Parrot.Stretch,
            (s, v) => s with { Monitor = s.Monitor with { Parrot = s.Monitor.Parrot with { Stretch = v } } });
        Number(p, "オウムを横に伸ばす（左右それぞれのドット数）", "左右へ絵を広げます。枠からはみ出す部分は切れます。", 0, 5, s => s.Monitor.Parrot.WidenPx,
            (s, v) => s with { Monitor = s.Monitor with { Parrot = s.Monitor.Parrot with { WidenPx = (int)v } } });
        Check(p, "オウムを虹色に塗る（RunCat の絵のときだけ）", "RunCat の影絵を虹色にします。もともと虹色の絵には影響しません。", s => s.Monitor.Parrot.Rainbow,
            (s, v) => s with { Monitor = s.Monitor with { Parrot = s.Monitor.Parrot with { Rainbow = v } } });
        ColorField(p, "オウムに添える数字の色", "オウムと一緒に表示する CPU 使用率の色です。", s => s.Monitor.Parrot.CpuTextColor,
            (s, v) => s with { Monitor = s.Monitor with { Parrot = s.Monitor.Parrot with { CpuTextColor = v } } });
        Check(p, "CPU 使用率のアイコンを出す", "CPU の使用率を独立した大きな数字で表示します。", s => s.Monitor.Cpu.Enabled,
            (s, v) => s with { Monitor = s.Monitor with { Cpu = s.Monitor.Cpu with { Enabled = v } } });
        ColorField(p, "CPU 使用率の色", "独立した CPU 使用率アイコンの数字の色です。", s => s.Monitor.Cpu.Color,
            (s, v) => s with { Monitor = s.Monitor with { Cpu = s.Monitor.Cpu with { Color = v } } });
        Check(p, "温度のアイコンを出す", "オフでは温度を標準のアプリアイコンに戻し、メモリ表示を消します。", s => s.Monitor.TempMemory.Enabled,
            (s, v) => s with { Monitor = s.Monitor with { TempMemory = s.Monitor.TempMemory with { Enabled = v } } });
        Check(p, "温度とメモリを別のアイコンにする", "オフでは温度とメモリを 1 つのアイコンに上下で表示します。", s => s.Monitor.TempMemory.Split,
            (s, v) => s with { Monitor = s.Monitor with { TempMemory = s.Monitor.TempMemory with { Split = v } } });
        ChoiceBox(p, "温度の読み方", "Core Temp を選ぶ場合は、Core Temp の起動と共有メモリの有効化が必要です。",
            [("acpi", "Windows の温度（acpi）"), ("coretemp", "Core Temp（coretemp）")], s => s.Monitor.TempMemory.Source,
            (s, v) => s with { Monitor = s.Monitor with { TempMemory = s.Monitor.TempMemory with { Source = v } } });
        ColorField(p, "温度の色", "温度の数字の色です。", s => s.Monitor.TempMemory.TempColor,
            (s, v) => s with { Monitor = s.Monitor with { TempMemory = s.Monitor.TempMemory with { TempColor = v } } });
        ColorField(p, "メモリの色", "メモリ使用率の数字の色です。", s => s.Monitor.TempMemory.MemoryColor,
            (s, v) => s with { Monitor = s.Monitor with { TempMemory = s.Monitor.TempMemory with { MemoryColor = v } } });
        BuildBattery(p);
    }

    private void BuildBattery(TableLayoutPanel p)
    {
        Check(p, "電池のアイコンを出す", "周辺機器の電池残量を通知領域に表示します。", s => s.Battery.Enabled,
            (s, v) => s with { Battery = s.Battery with { Enabled = v } });
        Check(p, "ロジクールの機器", "Logi Bolt につながった対応機器の電池を表示します。", s => s.Battery.Logitech,
            (s, v) => s with { Battery = s.Battery with { Logitech = v } });
        Check(p, "Bluetooth の機器", "Windows から電池残量を取得できる Bluetooth 機器を表示します。", s => s.Battery.Bluetooth,
            (s, v) => s with { Battery = s.Battery with { Bluetooth = v } });
        var warn = Number(p, "オレンジにする残量（%）", "この残量以下でオレンジにします。赤の残量より大きくしてください。", 2, 100, s => s.Battery.LowWarn,
            (s, v) => s with { Battery = s.Battery with { LowWarn = (int)v } });
        var hot = Number(p, "赤にする残量（%）", "この残量以下で赤にします。オレンジの残量より小さくしてください。", 1, 99, s => s.Battery.LowHot,
            (s, v) => s with { Battery = s.Battery with { LowHot = (int)v } });
        // 要確認: lowHot < lowWarn を常に満たすため、赤は 1〜99、オレンジは 2〜100 とし、もう片方を追従させる。
        warn.ValueChanged += (_, _) => { if (!loading && hot.Value >= warn.Value) hot.Value = warn.Value - 1; };
        hot.ValueChanged += (_, _) => { if (!loading && warn.Value <= hot.Value) warn.Value = hot.Value + 1; };
        Number(p, "ロジクールの機器を読み直す間隔（分）", "接続中の機器の電池残量を定期的に読み直します。", 1, 120, s => s.Battery.RefreshMinutes,
            (s, v) => s with { Battery = s.Battery with { RefreshMinutes = (int)v } });
        Number(p, "Bluetooth の機器を読み直す間隔（秒）", "Windows の電池情報を読み直す間隔です。", 10, 600, s => s.Battery.BluetoothPollSeconds,
            (s, v) => s with { Battery = s.Battery with { BluetoothPollSeconds = (int)v } });
        BuildBatteryKinds(p);
        var list = new CheckedListBox { CheckOnClick = true, Dock = DockStyle.Top, Height = 120, HorizontalScrollbar = true };
        var group = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); group.Controls.Add(list);
        var name = new TextBox { Dock = DockStyle.Top, AccessibleName = "出さない機器の名前" };
        group.Controls.Add(name);
        void AddName()
        {
            string value = name.Text;
            if (string.IsNullOrWhiteSpace(value)) return;
            int index = list.Items.Cast<DeviceName>().ToList().FindIndex(d => d.Name == value);
            if (index < 0) index = list.Items.Add(new DeviceName(value, deviceNames().Contains(value, StringComparer.Ordinal)));
            list.SetItemChecked(index, true); name.Clear();
        }
        group.Controls.Add(Button("名前を追加", AddName));
        name.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddName(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        Field(p, "出さない機器", group, "チェックした名前のアイコンを隠します。下の欄から名前を追加できます（大文字・小文字も区別します）。");
        loaders.Add(s =>
        {
            var known = deviceNames().ToHashSet(StringComparer.Ordinal);
            list.Items.Clear(); name.Clear();
            foreach (string item in known.Concat(s.Battery.Hidden).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
                list.Items.Add(new DeviceName(item, known.Contains(item)), s.Battery.Hidden.Contains(item, StringComparer.Ordinal));
        });
        List<string> Hidden() => list.CheckedItems.Cast<DeviceName>().Select(d => d.Name).ToList();
        snapshots.Add(Hidden);
        snapshots.Add(() => name.Text);
        readers.Add(s =>
        {
            // 要確認: 名前欄に入力したまま適用・OK を押した場合も追加し、入力を失わないようにする。
            AddName();
            return s with { Battery = s.Battery with { Hidden = Hidden() } };
        });
    }
    private sealed record DeviceName(string Name, bool Found)
    {
        public override string ToString() => Found ? Name : Name + "（今は見つかっていない）";
    }

    private void BuildOther()
    {
        var p = Page("その他");
        autoStart = new CheckBox { Text = "Windows の起動時に LogiGap を起動する", AutoSize = true };
        Field(p, "自動起動", autoStart, "チェックした時点で反映します。保存・キャンセルや既定に戻す操作の対象にはなりません。");
        autoStart.CheckedChanged += (_, _) =>
        {
            if (refreshingAutoStart) return;
            try { writeAutoStart(autoStart.Checked); }
            catch (Exception ex) { MessageBox.Show(this, "自動起動を変更できませんでした。\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            RefreshAutoStart();
        };
        Check(p, "横スクロールの記録を取る", "動きの調査用にログを残します。普段はオフにしてください。", s => s.Diagnostics.WheelTrace,
            (s, v) => s with { Diagnostics = s.Diagnostics with { WheelTrace = v } });
        Field(p, "設定ファイル", Button("設定ファイルを開く", () => openPath(configPath)), "設定を直接編集できます。画面の未保存の変更は含まれません。");
        Field(p, "ログ", Button("ログフォルダを開く", () => openPath(Path.Combine(AppContext.BaseDirectory, "logs"))), "動作記録のフォルダを開きます。");
        Field(p, "設定を戻す", Button("すべて既定に戻す", () =>
        {
            if (Ask(this, "画面の設定をすべて既定に戻しますか？\n保存するには OK または適用を押してください。",
                ("既定に戻す", DialogResult.Yes), ("キャンセル", DialogResult.Cancel)) == DialogResult.Yes) LoadControls(new AppSettings());
        }), "画面の項目を既定に戻します。自動起動・画面に出ない値は保持します。保存するまでは反映しません。");
    }
}
