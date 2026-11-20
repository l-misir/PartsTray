// マウスの肩代わりとウィンドウ操作の設定欄を作ります。
// 機種の説明や入力項目を変える場所です。
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class SettingsWindow
{
    private void BuildGap()
    {
        var p = Page("マウスの肩代わり");
        Field(p, null, new Label { AutoSize = true, Dock = DockStyle.Top,
            Text = "この機能はロジクールの MX Master 4（Logi Bolt 接続）専用です。Logi Options+ が動き出すまでの間だけ働きます" }, "");
        Check(p, "肩代わりする", "Options+ が動き出すまで、ボタンとポインタの速さを肩代わりします。", s => s.GapFiller.Enabled,
            (s, v) => s with { GapFiller = s.GapFiller with { Enabled = v } });
        Check(p, "Options+ に引き継いだら知らせる", "肩代わりの開始と、Options+ への引き継ぎを通知します。", s => s.GapFiller.NotifyOnSwitch,
            (s, v) => s with { GapFiller = s.GapFiller with { NotifyOnSwitch = v } });
        Number(p, "ポインタの速さ（普段）", "200〜8000、上下の矢印で 50 ずつ変更します。次の接続から有効です。", 200, 8000, s => s.GapFiller.Dpi.Primary,
            (s, v) => s with { GapFiller = s.GapFiller with { Dpi = s.GapFiller.Dpi with { Primary = (int)v } } }, step: 50);
        Number(p, "ポインタの速さ（切り替え後）", "速さの切り替えボタンで使う値です。次の接続から有効です。", 200, 8000, s => s.GapFiller.Dpi.Secondary,
            (s, v) => s with { GapFiller = s.GapFiller with { Dpi = s.GapFiller.Dpi with { Secondary = (int)v } } }, step: 50);
        string[] names = ["ホイールクリック（中ボタン）", "進む（親指の前側）", "戻る（親指の後側）", "ジェスチャーボタン（親指の上）", "親指パネル（ハプティック）"];
        for (int i = 0; i < GapSession.ButtonOrder.Count; i++)
        {
            string cid = $"0x{GapSession.ButtonOrder[i]:X4}";
            ChoiceBox(p, names[i], "肩代わり中の動作です。割り当ての変更は次の接続から有効です。",
                [("ToggleDpi", "ポインタの速さを切り替える"), ("Ctrl+C", "Ctrl+C（コピー）"), ("Ctrl+V", "Ctrl+V（貼り付け）"),
                 ("PrintScreen", "PrintScreen"), ("Win+Up", "Win+↑（最大化）"), ("None", "肩代わりしない")],
                s => SettingsText.ButtonChoice(s.GapFiller.Buttons.GetValueOrDefault(cid)),
                (s, v) => s with { GapFiller = SettingsText.SetButton(s.GapFiller, cid, v) });
        }
        ChoiceBox(p, "サムホイールの方式", "親指のホイールで横に動かす方式です。変更は次の接続から有効です。",
            [("hook", "横取りせずに送り直す（hook）"), ("divert", "マウスに直接頼む（divert）"), ("off", "肩代わりしない（off）")],
            s => s.GapFiller.ThumbWheel.Mode, (s, v) => s with { GapFiller = s.GapFiller with { ThumbWheel = s.GapFiller.ThumbWheel with { Mode = v } } });
        Number(p, "サムホイールの速さ（%）", "横に動く量を変えます。0% では動きません。", 0, 300, s => s.GapFiller.ThumbWheel.SpeedPercent,
            (s, v) => s with { GapFiller = s.GapFiller with { ThumbWheel = s.GapFiller.ThumbWheel with { SpeedPercent = v } } }, 1, 0.1m);
        ChoiceBox(p, "サムホイールの向き", "横に動かす向きを切り替えます。", [("1", "Options+ の『反転』オンと同じ（1）"), ("-1", "その逆（-1）")],
            s => s.GapFiller.ThumbWheel.DirectionSign.ToString(), (s, v) => s with { GapFiller = s.GapFiller with { ThumbWheel = s.GapFiller.ThumbWheel with { DirectionSign = v == "1" ? 1 : -1 } } });
        var mode = ChoiceBox(p, "動きの滑らかさ", "送り直す方式で、加速・減速するか均等に動かすかを選びます。", [("spring", "バネ（spring）"), ("linear", "均等（linear）")],
            s => s.GapFiller.ThumbWheel.SmoothMode, (s, v) => s with { GapFiller = s.GapFiller with { ThumbWheel = s.GapFiller.ThumbWheel with { SmoothMode = v } } });
        var spring = Number(p, "バネの時間（ミリ秒）", "大きいほど、ゆっくり動き出してゆっくり止まります。", 10, 500, s => s.GapFiller.ThumbWheel.SpringMs,
            (s, v) => s with { GapFiller = s.GapFiller with { ThumbWheel = s.GapFiller.ThumbWheel with { SpringMs = (int)v } } });
        var smooth = Number(p, "均等に分ける時間（ミリ秒）", "0 はすぐに送り、0 以外は送る間隔以上にします。", 0, 200, s => s.GapFiller.ThumbWheel.SmoothMs,
            (s, v) => s with { GapFiller = s.GapFiller with { ThumbWheel = s.GapFiller.ThumbWheel with { SmoothMs = (int)v } } });
        var interval = Number(p, "均等に分ける間隔（ミリ秒）", "小分けにして送る間隔です。バネもこの保存値を使います。", 4, 50, s => s.GapFiller.ThumbWheel.SmoothIntervalMs,
            (s, v) => s with { GapFiller = s.GapFiller with { ThumbWheel = s.GapFiller.ThumbWheel with { SmoothIntervalMs = (int)v } } });
        void EnableMode() { bool isSpring = (mode.SelectedItem as Choice)?.Value == "spring"; spring.Enabled = isSpring; smooth.Enabled = interval.Enabled = !isSpring; }
        mode.SelectedIndexChanged += (_, _) => EnableMode(); loaders.Add(_ => EnableMode());
        // 要確認: 相互制約は入力側を優先し、均等時間が 0 以外で間隔未満になった場合は時間を間隔まで上げる。
        void FitTiming() { if (!loading && smooth.Value != 0 && smooth.Value < interval.Value) smooth.Value = interval.Value; }
        smooth.ValueChanged += (_, _) => FitTiming(); interval.ValueChanged += (_, _) => FitTiming();
        Number(p, "引き継ぎ後の後片付けまでの時間（秒）", "Options+ が設定しなかったボタンなどを確認するまで待つ時間です。", 15, 300, s => s.GapFiller.CleanupDelaySeconds,
            (s, v) => s with { GapFiller = s.GapFiller with { CleanupDelaySeconds = (int)v } });
    }

    private void BuildWindows()
    {
        var p = Page("ウィンドウ・ホイール");
        Check(p, "Shift＋クリックで最前面に固定", "タイトルバーを Shift＋左クリックすると、最前面への固定を切り替えます。", s => s.TitleBar.ShiftClick != "None",
            (s, v) => s with { TitleBar = s.TitleBar with { ShiftClick = v ? "ToggleTopMost" : "None" } });
        Check(p, "Ctrl＋クリックでそのアプリのフォルダを開く", "タイトルバーを Ctrl＋左クリックすると、実行ファイルの場所を表示します。", s => s.TitleBar.CtrlClick != "None",
            (s, v) => s with { TitleBar = s.TitleBar with { CtrlClick = v ? "OpenExecutableFolder" : "None" } });
        Check(p, "中クリックで閉じる", "タイトルバーを中クリックすると、そのウィンドウを閉じます。", s => s.TitleBar.MiddleClick != "None",
            (s, v) => s with { TitleBar = s.TitleBar with { MiddleClick = v ? "CloseWindow" : "None" } });
        Check(p, "最小化ボタンの右クリックで通知領域にしまう", "しまったウィンドウは通知領域のアイコンから戻せます。", s => s.TitleBar.MinimizeButtonRightClick != "None",
            (s, v) => s with { TitleBar = s.TitleBar with { MinimizeButtonRightClick = v ? "MinimizeToTray" : "None" } });
        Check(p, "スクロールバーの上では速くスクロール", "標準の縦スクロールバーにカーソルを置いたときだけ速く動かします。", s => s.Wheel.FastScrollOnScrollBar,
            (s, v) => s with { Wheel = s.Wheel with { FastScrollOnScrollBar = v } });
        Number(p, "速くスクロールする倍率", "スクロールバー上での動く量を 1〜100 倍にします。", 1, 100, s => s.Wheel.FastScrollMultiplier,
            (s, v) => s with { Wheel = s.Wheel with { FastScrollMultiplier = (int)v } });
        Check(p, "カーソルの下のウィンドウをスクロール", "前面ウィンドウ内のカーソル下の枠へ入力先を移します。背景アプリは前面に出しません。", s => s.Wheel.FocusUnderCursor,
            (s, v) => s with { Wheel = s.Wheel with { FocusUnderCursor = v } });
        Check(p, "ダイアログの後ろで操作できないウィンドウも動かす", "見えているタイトルバーを左ドラッグして、操作できない親ウィンドウを移動します。", s => s.MoveDisabledWindows,
            (s, v) => s with { MoveDisabledWindows = v });
    }
}
