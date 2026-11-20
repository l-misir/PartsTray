# 指示書M：設定用のウィンドウを作る

## 0. この指示書の目的

### 0.1 ユーザーの要望（11/20）

今の LogiGap の設定は `config.json` を手で書き換えるしかない。ユーザーの言葉：「個人的に json だとめんどくさいので（設定用のウィンドウが）ほしいです」。
また、1 人に配る予定があり、その人も JSON を書き換えずに設定できるようにしたい。

### 0.2 方針

- WinForms のウィンドウ「LogiGap の設定」を作る。タブで分け、今ある設定を**全部**画面から変えられるようにする（§3）
- 保存は今の `AppSettings.Save` で `config.json` に書く。アプリへの反映は、今の「設定ファイルの変化を 1 秒ごとに見て読み直す」仕組み（`TrayContext.Reload`）に任せる
- 値の正しさの判断は `AppSettings.Validate()` を正とする。画面の入力欄は、Validate が通る範囲だけを選べる・入力できる作りにする（数値の上限下限、選択肢）
- 画面の文字は全部日本語。専門用語を避け、各項目の下に小さな灰色の字で「何が変わるか」を 1 行で書く
- 設定ファイルの形（キーの名前・値の形）は変えない。既存のテストと設定の読み込みを壊さない

**作業の順番**：本体（§2〜§5）を先に書いてビルドを通し、その後でテスト（§6）、最後に README（§7）。

## 1. 変更してよいファイル

- 新規 `app/LogiGap/SettingsWindow.cs`（ウィンドウ本体。大きくなるならタブごとに分けてよい：`SettingsWindow.*.cs`）
- 新規 `app/LogiGap.Core/SettingsText.cs`（画面と設定の値の変換など、テストできる純粋な処理。名前は任せる）
- `app/LogiGap/Program.cs`（ウィンドウを開く入口）
- `app/LogiGap/BatteryModule.cs`（今見えている機器の名前を外に渡す読み取り専用のメソッドを足すだけ）
- `app/LogiGap.Tests/` の下（テストの追加）
- `app/README.md`

ほかのファイルは変えない。`app/publish/` と `logs/` には触れない。`app/LogiGap.Core/Settings.cs` と `GestureSettings.cs` は**変えない**（設定の形と検査はそのまま使う）。

## 2. 開き方（Program.cs）

- 右クリックメニューの一番上（状態の行の次）に「設定…」を足す。クリックでウィンドウを開く
- 本体のアイコン（今は温度を表示しているアイコン）の左クリック：今は `config.json` をエディタで開いている（`clickTimer` → `Open(configPath)`）。これを**設定ウィンドウを開く**に変える。ダブルクリックと中クリックの扱いは今のまま
- 2 つ目の LogiGap を起動したときの合図（`show` の EventWaitHandle）：今は `config.json` を開いている。これも設定ウィンドウを開くに変える
- 「設定ファイルを開く」「ログフォルダを開く」のメニューは残す
- ウィンドウは 1 つだけ。開いているときにもう一度開こうとしたら、前に出して（最小化されていれば元に戻して）アクティブにする
- ウィンドウを閉じたら破棄する（次に開くときは作り直し、その時点の `config.json` を読む）
- 開くとき `config.json` を読む（`AppSettings.Load`）。読めない・不正なときは、その理由をメッセージで出し、「既定の設定で開く」か「やめる」を選ばせる

## 3. ウィンドウの中身

### 3.1 全体

- タイトル「LogiGap の設定」。タスクバーに出す（`ShowInTaskbar = true`）。アイコンはアプリのアイコン
- タブ（`TabControl`）。各タブの中身は縦に並べ、はみ出す分はスクロールできる（`AutoScroll`）
- 下に「OK」「キャンセル」「適用」。OK＝保存して閉じる。適用＝保存して開いたまま。キャンセル＝保存せず閉じる。変更があるのにキャンセル・×で閉じるときは「変更を保存しますか？（保存／保存しない／キャンセル）」と聞く
- 拡大率 125%・150%・200% で文字や部品が重ならず、切れないこと（`AutoScaleMode.Dpi`、配置は `TableLayoutPanel` / `FlowLayoutPanel` で組み、座標の決め打ちをしない）。フォントは「Yu Gothic UI」9pt
- 最初の大きさは 125% で横 720・縦 640 くらい。大きさを変えられ、最小の大きさを決めておく
- 各項目の下に説明（灰色、小さめ）。例：「Options+ が動き出すまでの間だけ、LogiGap がボタンとポインタの速さを肩代わりします」

### 3.2 タブ「マウスの肩代わり」（`gapFiller`）

| 画面 | 設定 | 部品と範囲 |
|---|---|---|
| 肩代わりする | `enabled` | チェック |
| Options+ に引き継いだら知らせる | `notifyOnSwitch` | チェック |
| ポインタの速さ（普段） | `dpi.primary` | 数値 200〜8000、50 刻み |
| ポインタの速さ（切り替え後） | `dpi.secondary` | 同上 |
| ボタンの割り当て（5 行） | `buttons` | 1 行 1 ボタン（下の表）。選択肢は下の表 |
| サムホイールの方式 | `thumbWheel.mode` | 「横取りせずに送り直す（hook）」「マウスに直接頼む（divert）」「肩代わりしない（off）」 |
| サムホイールの速さ | `thumbWheel.speedPercent` | 数値 0〜300 %（小数 1 桁まで） |
| サムホイールの向き | `thumbWheel.directionSign` | 「Options+ の『反転』オンと同じ（1）」「その逆（-1）」 |
| 動きの滑らかさ | `thumbWheel.smoothMode` | 「バネ（spring）」「均等（linear）」 |
| バネの時間 | `thumbWheel.springMs` | 10〜500 ms（バネのときだけ有効にする） |
| 均等に分ける時間・間隔 | `thumbWheel.smoothMs` / `smoothIntervalMs` | 0〜200 ms / 4〜50 ms（均等のときだけ有効。smoothMs が 0 でなければ smoothMs ≥ 間隔） |
| 引き継ぎ後の後片付けまでの時間 | `cleanupDelaySeconds` | 15〜300 秒 |

`mouseWpid`（0xB042）と `ownSwid`（5）は安全のため固定なので、画面に出さない（値はそのまま保つ）。

ボタンの行（`GapSession.ButtonOrder` の順。名前はリポジトリ直下の `README.md` の「ユーザーの Options+ 設定」の表による）：

| CID | 画面の名前 |
|---|---|
| 0x0052 | ホイールクリック（中ボタン） |
| 0x0056 | 進む（親指の前側） |
| 0x0053 | 戻る（親指の後側） |
| 0x00C3 | ジェスチャーボタン（親指の上） |
| 0x01A0 | 親指パネル（ハプティック） |

選択肢：「ポインタの速さを切り替える」（`ToggleDpi`）、「Ctrl+C（コピー）」「Ctrl+V（貼り付け）」「PrintScreen」「Win+↑（最大化）」（`Keys`）、「肩代わりしない」（`buttons` からそのキーを消す）。
「肩代わりしない」を出してよいのは、`buttons` にキーが無いときに `GapSession` がそのボタンを横取りしない作りになっている場合だけ。`GapSession` を読んで確かめ、そうでなければ選択肢に入れない（報告に書く）。

### 3.3 タブ「ジェスチャー」（`gesture`）

| 画面 | 設定 | 部品と範囲 |
|---|---|---|
| ジェスチャーを使う | `enabled` | チェック |
| 動いたと見なす距離 | `strokeDistancePx` | 1〜1000 px |
| 始めるまでの待ち時間 | `startTimeoutMs` | 1〜2000 ms |
| 説明を出すまでの時間 | `tooltipDelayMs` | 1〜60000 ms |
| 押し続けて連打 | `rapidFire` | チェック（意味は README の B の節で確かめて説明を書く） |
| 線を描く | `trail.enabled` | チェック |
| 線の太さ | `trail.widthPx` | 1〜100（小数 1 桁まで） |
| 線の色（認識できた／できない） | `trail.validColor` / `invalidColor` | 色のボタン（§3.7） |
| ジェスチャーの一覧 | `gestures` | 表（下） |
| ブラウザーとして扱うアプリ | `browserProcesses` | 複数行の入力（1 行 1 つ、`chrome.exe` の形） |
| ジェスチャーを使わないアプリ | `excludedProcesses` | 同上 |
| エクスプローラーで選択中は飛ばす | `explorerSkipSelectedItems` | チェック（意味は README で確かめて説明を書く） |

ジェスチャーの一覧（`DataGridView`、行の追加・削除ができる）：
- 「形」の列：矢印（←→↑↓）で表示する。設定の中は `L` `R` `U` `D` の文字列。入力は、矢印の文字・`LRUD` のどちらでも受け付け、表示は矢印に直す。行を選んで押す「← → ↑ ↓ 1 つ消す」のボタンでも作れるようにする
- 「動作」の列：「戻る（Back）」「進む（Forward）」「更新（Refresh）」「閉じる（Close）」「最小化（Minimize）」
- 検査（保存の前）：形は 1〜64 文字、同じ向きが続かない、同じ形が 2 行ない、行は 256 まで。違反した行は赤くし、保存しない

### 3.4 タブ「ウィンドウ・ホイール」（`titleBar`・`wheel`・`moveDisabledWindows`）

- タイトルバー（4 つのチェック。オン＝その動作、オフ＝`None`）
  - Shift＋クリックで最前面に固定（`shiftClick`：`ToggleTopMost`）
  - Ctrl＋クリックでそのアプリのフォルダを開く（`ctrlClick`：`OpenExecutableFolder`）
  - 中クリックで閉じる（`middleClick`：`CloseWindow`）
  - 最小化ボタンの右クリックで通知領域にしまう（`minimizeButtonRightClick`：`MinimizeToTray`）
- ホイール
  - スクロールバーの上では速くスクロール（`fastScrollOnScrollBar`）、その倍率（`fastScrollMultiplier`：1〜100）
  - カーソルの下のウィンドウをスクロール（`focusUnderCursor`）
- `moveDisabledWindows`：README で意味を確かめ、分かりやすい名前と説明を付けてチェックにする

### 3.5 タブ「通知領域のアイコン」（`monitor`・`battery`）

モニター：

| 画面 | 設定 | 部品と範囲 |
|---|---|---|
| CPU・温度・メモリのアイコンを出す | `monitor.enabled` | チェック |
| 更新の間隔 | `monitor.sampleIntervalMs` | 100〜5000 ms |
| 数字の形 | `monitor.digitStyle` | 「Core Temp 風（thin）」「点の文字を伸ばす（wide）」 |
| オウムを出す | `parrot.enabled` | チェック |
| オウムに CPU 使用率の数字を添える | `parrot.cpuText` | 「添えない（none）」「下（bottom）」「上（top）」 |
| オウムを縦に伸ばす | `parrot.stretch` | チェック |
| オウムを横に伸ばす | `parrot.widenPx` | 0〜5 ドット（左右それぞれ） |
| オウムを虹色に塗る（RunCat の絵のときだけ） | `parrot.rainbow` | チェック |
| オウムに添える数字の色 | `parrot.cpuTextColor` | 色 |
| CPU 使用率のアイコンを出す／色 | `cpu.enabled` / `cpu.color` | チェック／色 |
| 温度のアイコンを出す | `tempMemory.enabled` | チェック |
| 温度とメモリを別のアイコンにする | `tempMemory.split` | チェック |
| 温度の読み方 | `tempMemory.source` | 「Windows の温度（acpi）」「Core Temp（coretemp）」 |
| 温度の色／メモリの色 | `tempMemory.tempColor` / `memoryColor` | 色 |

電池：

| 画面 | 設定 | 部品と範囲 |
|---|---|---|
| 電池のアイコンを出す | `battery.enabled` | チェック |
| ロジクールの機器 | `battery.logitech` | チェック |
| Bluetooth の機器 | `battery.bluetooth` | チェック |
| オレンジにする残量 | `battery.lowWarn` | 1〜100 % |
| 赤にする残量 | `battery.lowHot` | 1〜100 %（lowWarn より小さい） |
| ロジクールの機器を読み直す間隔 | `battery.refreshMinutes` | 1〜120 分 |
| Bluetooth の機器を読み直す間隔 | `battery.bluetoothPollSeconds` | 10〜600 秒 |
| 出さない機器 | `battery.hidden` | 一覧（下） |

`battery.pictureHeight` は今の描き方では使っていないので、画面に出さない（値はそのまま保つ）。

出さない機器の一覧：今 LogiGap が見つけている機器の名前（`BatteryModule` から読み取り専用でもらう。§5）をチェックの一覧で出し、チェックした名前を `hidden` にする。`hidden` にあるが今は見つかっていない名前も、一覧に残して表示する（「（今は見つかっていない）」と添える）。名前を手で足す欄も付ける。

### 3.6 タブ「その他」

- Windows の起動時に LogiGap を起動する（今の右クリックメニューの「Windows 起動時に自動起動」と同じ処理。レジストリの `Run` の `LogiGap`。片方を変えたらもう片方にも反映される＝同じ値を読む）。これは保存ボタンを待たず、チェックした時点で反映してよい（説明にそう書く）
- 横スクロールの記録を取る（`diagnostics.wheelTrace`）
- 「設定ファイルを開く」「ログフォルダを開く」のボタン
- 「すべて既定に戻す」ボタン：確認してから、画面の値を `new AppSettings()` の値に戻す（保存はまだしない。OK／適用で保存）

### 3.7 色の選び方

- 色の見本を塗ったボタン＋`#RRGGBB` の文字。クリックで `ColorDialog`。選んだ色を `#RRGGBB`（大文字）で持つ
- 文字の欄に直接 `#RRGGBB` を打ってもよい。形が違えば赤くして保存しない

## 4. 保存

- 画面の値から、読み込んだ `AppSettings` を `with` で作り直す（画面に出さない値・未知の項目 `Modules` はそのまま保つ）
- 保存の前に `Validate()`。例外が出たら、その文をメッセージで出し、保存しない（できれば該当するタブを開く）
- 保存は `AppSettings.Save(configPath, value)`。書き込みに失敗したら（ほかのアプリが開いている等）メッセージで知らせる
- 保存した後、反映は `TrayContext.Reload`（1 秒ごと）に任せる。保存の直後に `Reload` を呼んでもよい（呼ぶなら、二重に読まないこと）
- ウィンドウを開いている間に `config.json` が外で書き換えられた場合：保存のとき、開いたときから書き換えられていれば「ほかで変更されています。上書きしますか？」と聞く

## 5. BatteryModule

- 今見つけている機器の名前の一覧を返す読み取り専用のメソッドを足す（UI スレッドから呼ぶ。`devices` の値の `Name`、重複なし、名前順）。描き方・判定は変えない

## 6. テスト（app/LogiGap.Tests）

画面そのものはテストしない（WinForms を起動しない）。Core に置いた純粋な処理をテストする。例：
- ジェスチャーの形の変換：`"LUR"` ⇔ `"←↑→"`、矢印と文字の混在の受け付け、不正な文字・同じ向きの連続の検出
- ボタンの選択肢 ⇔ `ButtonSettings`（`ToggleDpi`、4 つの `Keys`、肩代わりしない＝キーなし）の変換
- 色の文字の検査と大文字化
- 画面の値から作った `AppSettings` が `Validate()` を通ること、既定値をそのまま保存して読み直すと同じになること、`Modules`（未知の項目）が保たれること
- 既存の 491 件は全部合格のまま

## 7. README

`app/README.md` に「設定ウィンドウ（指示書 M）」の節を足す：開き方（右クリックの「設定…」、温度のアイコンの左クリック、2 つ目の起動）、タブの中身の一覧、保存と反映のしかた、`config.json` を直接書き換える方法も残っていること。

## 8. してはいけないこと

- アプリ・HID・フック・入力の送信・通知・トレイのアイコン・ウィンドウを実際に動かすプログラムを起動しない（画面の確かめは Claude が実機で行う）
- インターネットから何もダウンロードしない。パッケージを足さない
- 設定の形（JSON のキー・値）と検査（`Validate`）を変えない。つなぎ役・ジェスチャー・アイコンの動きを変えない

## 9. 完了報告の形式（日本語）

1. 変えたファイルと、それぞれの要点
2. 画面の構成（タブごとの項目の一覧）と、§3 から変えた・省いた所とその理由
3. 「肩代わりしない」を選択肢に入れたか（§3.2）と、その根拠
4. `// 要確認:` を残した箇所の一覧と理由
5. `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` の結果（テストの件数と合否）
6. 実機で確かめてほしいこと
