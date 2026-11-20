# 指示書J：通知領域に「オウム（CPU 使用率）」と「温度・メモリ」のアイコンを出す

## 0. この指示書の目的

### 0.1 ユーザーの要望

今は RunCat（白いオウム）と Core Temp のアイコンで、通知領域が埋まっている。ピン留めしたアプリのボタンが小さくなって使いづらい。LogiGap に次の 2 つのアイコンを作り、RunCat と Core Temp のアイコンを消したい。

| アイコン | 中身 |
|---|---|
| オウム | オウムの絵が CPU 使用率に合わせて速く動く。虹色に光る。CPU 使用率の数字を、絵の上か下に書く |
| 温度・メモリ | 上の段に CPU 温度（全コアの平均）、下の段にメモリ使用率 |

周辺機器の電池は、別の指示書（K）で作る。この指示書では扱わない。

### 0.2 実機の条件（Claude が確かめた）

| 項目 | 値 |
|---|---|
| 画面の拡大率 | 2 画面とも 125%（AppliedDPI=120） |
| 通知領域のアイコンの大きさ | `GetSystemMetricsForDpi(SM_CXSMICON=49, dpi)`：96→16、120→**20**、144→24、192→32 ピクセル。正方形 1 つだけで、横長にはできない |
| OS | Windows 11 ビルド 26300 |
| CPU 温度 | Core Temp を動かしたまま、その共有メモリから読む（Core Temp はソース非公開で改変できない。温度を直接読むにはドライバーと管理者権限が要る） |

### 0.3 方針

- 値の計算・Core Temp の共有メモリの解釈・数字のビットマップフォント・速さの式など、**純粋な処理は `LogiGap.Core` に置く**（テストするため）。描画と Windows API は `LogiGap` に置く
- 今の LogiGap のアイコン（`SystemIcons.Application` のもの）を、**温度・メモリのアイコンに置き換える**。メニュー・クリック・通知（バルーン）はそのまま使う。これでアイコンの数が増えない
- オウムは、別の `NotifyIcon` を 1 つ足す。右クリックで同じメニューを出す
- パッケージは追加しない。CPU 使用率は `GetSystemTimes`、メモリは `GlobalMemoryStatusEx`（どちらも kernel32）で読む

**作業の順番**：本体のコード（§2〜§6）を先に書き、ビルドが通ってからテスト（§7）と README（§8）を書く。

## 1. 変更してよいファイル

- `app/LogiGap.Core/Settings.cs`
- 新規 `app/LogiGap.Core/Monitor.cs`（純粋な処理。名前は変えてよい）
- 新規 `app/LogiGap/MonitorModule.cs`（読み取り・描画・アイコン。名前は変えてよい）
- `app/LogiGap/Program.cs`（`TrayContext` のアイコンの置き換え）
- `app/LogiGap/LogiGap.csproj`（`Assets` を出力にコピーする設定だけ）
- `app/LogiGap.Tests/` の下（新しいテストの追加）
- `app/README.md`

`app/LogiGap/Assets/parrot/` には、Claude が後で絵（`parrot_0.png`〜`parrot_9.png`、RunCat365 の Apache-2.0 の素材）とライセンスの文書を置く。**Codex は絵をダウンロードしない**。絵が無いときの代わりの表示（§5.3）を必ず作ること。

ほかのファイルは変えない。`app/publish/` と `logs/` には触れない。

## 2. 設定（Settings.cs）

`AppSettings` に最上位の `Monitor` を足す。

```csharp
public sealed record MonitorSettings
{
    public bool Enabled { get; init; } = true;
    public ParrotSettings Parrot { get; init; } = new();
    public TempMemorySettings TempMemory { get; init; } = new();
    public int SampleIntervalMs { get; init; } = 1000;      // 500〜5000
}
public sealed record ParrotSettings
{
    public bool Enabled { get; init; } = true;
    public string CpuText { get; init; } = "bottom";        // "bottom" / "top" / "none"
    public bool Rainbow { get; init; } = true;
}
public sealed record TempMemorySettings
{
    public bool Enabled { get; init; } = true;
    public int TempWarn { get; init; } = 80;                // 以上で橙（°C）
    public int TempHot { get; init; } = 90;                 // 以上で赤（°C）
    public int MemoryWarn { get; init; } = 85;              // 以上で橙（%）
}
```

JSON は `"monitor": { "enabled": true, "parrot": { … }, "tempMemory": { … }, "sampleIntervalMs": 1000 }`。`Validate` で次を確かめ、違えば `InvalidDataException`（文言は自由。日本語で、どの項目かわかるように）。

- `Monitor`・`Parrot`・`TempMemory` が null でない
- `SampleIntervalMs` が 500〜5000
- `CpuText` が `bottom` / `top` / `none`
- `TempWarn` < `TempHot`、どちらも 30〜120。`MemoryWarn` が 1〜100

`TrayContext` の設定エラー時の安全な設定では、`Monitor` は既定のままでよい。

## 3. 値の読み取り

### 3.1 CPU 使用率（Core に計算、app に API）

- `GetSystemTimes(out idle, out kernel, out user)` を 1 秒ごと（`SampleIntervalMs`）に読む。kernel には idle が含まれる
- Core の純粋な関数：前回と今回の値から `使用率 = 100 × (1 − Δidle / (Δkernel + Δuser))`。0〜100 に収める。分母が 0 なら前回の値
- RunCat365 も、ビルド 26100 以降は「% Processor Time」を使っている。`GetSystemTimes` はこれと同じ考え方

### 3.2 メモリ使用率

`GlobalMemoryStatusEx` の `dwMemoryLoad`（%）。ツールチップ用に、使用量と総量（GB、小数 1 桁）も持つ。

### 3.3 CPU 温度（Core Temp の共有メモリ）

- 名前 `CoreTempMappingObjectEx` を `OpenFileMapping(FILE_MAP_READ, false, 名前)` で開く。失敗したら `Global\CoreTempMappingObjectEx` も試す。開けなければ「温度なし」
- `MapViewOfFile` で読み、**バイト列として Core の解釈関数に渡す**（テストできるように）。開いたままにして毎回読み直してよいが、Core Temp が終了・再起動したら開き直す（読めない・値がおかしいときに閉じて、次の周期で開き直す）
- 構造体（Core Temp の開発者向けページ、`core_temp_shared_data_ex`。メンバーは 4 バイト境界）。オフセットは Claude が計算した値：

| オフセット | 型 | 名前 |
|---|---|---|
| 0 | uint[256] | uiLoad |
| 1024 | uint[128] | uiTjMax |
| 1536 | uint | uiCoreCnt |
| 1540 | uint | uiCPUCnt |
| 1544 | float[256] | fTemp |
| 2568 | float ×4 | fVID, fCPUSpeed, fFSBSpeed, fMultiplier |
| 2584 | char[100] | sCPUName |
| 2684 | byte | ucFahrenheit（1 = 華氏） |
| 2685 | byte | ucDeltaToTjMax（1 = TjMax までの差） |
| 2686 | byte | ucTdpSupported |
| 2687 | byte | ucPowerSupported |
| 2688 | uint | uiStructVersion |
| 2692 | uint[128] | uiTdp |
| 3204 | float[128] | fPower |
| 3716 | float[256] | fMultipliers |
| 4740 | （合計の大きさ） | |

- 平均温度の計算（Core の純粋な関数）：
  - コア数 `n = uiCoreCnt × uiCPUCnt`。0 または 256 を超えるなら「温度なし」
  - 各コア `i` の値 `t = fTemp[i]`。`ucDeltaToTjMax == 1` なら `t = uiTjMax[i / uiCoreCnt] − fTemp[i]`
  - `ucFahrenheit == 1` なら `t = (t − 32) × 5 / 9`
  - 0℃以下・150℃以上・NaN の値は捨てる。残りの平均と最大を返す。1 つも残らなければ「温度なし」
  - 読み込むバイト列が 2688 バイトより短ければ「温度なし」
- **電力の値（fPower など）は使わない**（ユーザーは電力消費の表示を消したい）

## 4. 数字のビットマップフォント（Core）

20×20 に 2 段の数字を入れるので、文字はドット単位で作る（アンチエイリアスの文字はにじむ）。

- 数字 0〜9 と `-` の点の並び（bool の 2 次元配列）を、**高さ 5（幅 3）・高さ 7（幅 4）・高さ 9（幅 5）**の 3 種類持つ
- 文字の間は 1 ドット空ける
- 関数：`Glyphs(string text, int height)` → 点の並び。幅と高さを返す
- 大きさの選び方（Core の純粋な関数）：アイコンの一辺 `size` と、その段に使える高さから、収まる最大の高さを選ぶ
  - 温度・メモリ：1 段の高さ = `(size − 1) / 2`。16→7、20→9、24→11（9 を使う）、32→15（9 を使う）
  - オウム：数字の段は高さ 5（`size` が 24 以上なら 7）。残りを絵に使う
- 3 桁（100 以上）が幅に収まらなければ、表示は `99` にする（ツールチップは正しい値）

## 5. アイコンの描画（app）

### 5.1 共通

- アイコンの一辺：タスクバー（`Shell_TrayWnd`）の DPI（`GetDpiForWindow`、取れなければ `GetDpiForSystem`）で `GetSystemMetricsForDpi(49, dpi)`。毎回の描画で求めてよい
- 色：`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` の `SystemUsesLightTheme` が 0（暗い）なら文字は白、1 なら黒。5 秒ごとに読み直してよい
- 描画は 32 ビット ARGB の `Bitmap`（背景は透明）に点を打ち、`GetHicon` でアイコンにする。**前のアイコンのハンドルは、新しいものに差し替えた後で `DestroyIcon` する**（GDI のハンドルを漏らさない）
- 描画とアイコンの差し替えは UI スレッド（`System.Windows.Forms.Timer`）で行う

### 5.2 温度・メモリのアイコン（今の LogiGap のアイコンを置き換える）

- 上の段：CPU 平均温度（℃、整数に四捨五入）。温度が無ければ `--`
- 下の段：メモリ使用率（%）
- 各段は横の中央にそろえる
- 色：温度が `TempWarn` 以上なら橙（#FF9900）、`TempHot` 以上なら赤（#FF3B30）。メモリが `MemoryWarn` 以上なら橙。それ以外はテーマの色
- ツールチップ（`NotifyIcon.Text`、127 文字まで）：1 行目は今の LogiGap の状態の文（`LogiGap: 肩代わり中` など）。2 行目に `CPU 52℃（最高 60℃）・メモリ 63%（10.1/16.0 GB）`。温度が無ければ `CPU 温度なし（Core Temp の共有メモリが見つからない）`
- 1 秒ごと（`SampleIntervalMs`）に描き直す
- `TrayContext` の `icon` をそのまま使う（メニュー・左クリックで設定を開く・中クリックでジェスチャーの一時停止・バルーン通知は変えない）。今 `StateChanged` が `icon.Text` を書いている箇所は、状態の文を保存して、ツールチップの組み立てに使う形にする
- `Monitor.Enabled` か `TempMemory.Enabled` が false なら、今までどおり `SystemIcons.Application` と状態の文だけに戻す

### 5.3 オウムのアイコン

- 新しい `NotifyIcon`。右クリックで `TrayContext` と同じ `ContextMenuStrip` を出す
- 絵：`AppContext.BaseDirectory` の `Assets/parrot/parrot_0.png`〜`parrot_9.png` を起動時に読む（10 枚）。白い影絵（透明の背景）を想定する。**1 枚でも読めなければ**、代わりに円を描いた 1 枚の絵を使い、ログに 1 回だけ `オウムの絵が見つからないため、代わりの丸を表示します` と書く
- 絵の置き場所：数字の段を除いた範囲に、縦横比を保って最大の大きさで中央に置く（`InterpolationMode.HighQualityBicubic`）。`CpuText` が `none` なら全体を使う
- 色：絵の不透明度（アルファ）を残し、色だけ塗り替える
  - `Rainbow` が true：色相を `(コマ番号 × 36 + 経過時間ミリ秒 / 10) mod 360` にした、彩度 100%・明度 55% 程度の色
  - false：テーマの文字色
- 数字：CPU 使用率（整数）を、`CpuText` に合わせて上か下に、テーマの文字色で描く
- コマ送り：次のコマまでの間隔（Core の純粋な関数）= `500 / max(1, CPU使用率 / 5)` ミリ秒を 25〜500 に収める（0〜5% で 500ms、100% で 25ms。RunCat に近い式）
- ツールチップ：`CPU 23%`
- `Parrot.Enabled` か `Monitor.Enabled` が false なら、オウムのアイコンを消す（`Visible = false`）

### 5.4 設定の再読込と終了

- `IFeatureModule.SettingsChanged` で、設定をすぐに反映する（アイコンを出す・消す、色のしきい値、位置）
- `Dispose` で、タイマーを止め、オウムの `NotifyIcon` を消して破棄し、残っているアイコンのハンドルをすべて `DestroyIcon` する。Core Temp の共有メモリを閉じる

## 6. csproj

`app/LogiGap/LogiGap.csproj` に、`Assets\**` を出力にコピーする設定を足す（`<None Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />`）。`Assets` フォルダーが無くてもビルドが通ること。

## 7. テスト（本体のコードが通ってから）

Core の純粋な処理だけをテストする。

1. CPU 使用率：Δidle=50・Δkernel=100（idle を含む）・Δuser=100 → 75%。分母 0 → 前回の値。範囲外は 0〜100 に収まる
2. Core Temp の解釈：4740 バイトの配列を組み立てて、次を確かめる
   - 2 コア 1 CPU、50℃・60℃ → 平均 55、最大 60
   - `ucDeltaToTjMax = 1`、TjMax 100、値 40・50 → 平均 55
   - `ucFahrenheit = 1`、122℉・140℉ → 平均 55
   - コア数 0・コア数 300・NaN・200℃ を含む場合の扱い（§3.3）
   - 2687 バイトの配列 → 温度なし
3. フォント：0〜9 と `-` が 3 種類の高さすべてにあり、幅・高さが決まった値。`"55"` の幅は 高さ 7 で 9（4+1+4）
4. 大きさの選び方：温度・メモリで 16→7、20→9、32→9。オウムで 20→5、24→7
5. 3 桁の扱い：`100` が幅 20・高さ 9 に収まらなければ `99`
6. コマ送りの間隔：0%→500、5%→500、50%→50、100%→25
7. 設定：既定値、JSON の読み書き、範囲外の値の例外

## 8. README

指示書I の節の後に、指示書J の節を足す。

- 0.1 と 0.2 の表
- Core Temp の使い方：Core Temp を動かしたままにする。Core Temp 側のアイコンは Core Temp の設定で消す。共有メモリが見つからないときは、Core Temp の詳細設定の共有メモリの項目を確かめる
- `monitor` の設定の意味
- オウムの絵は RunCat365（Apache License 2.0）の素材で、`Assets/parrot/` にライセンスの文書を置いていること
- Windows 11 は新しいアイコンを最初は「隠れたアイコン」に入れるので、設定 → 個人用設定 → タスク バー → その他のシステム トレイ アイコン で表示をオンにすること

## 9. 守ること

- `app/` の外は変えない（`logs/` は読むだけ。`app/publish/` には触れない）
- アプリ・HID・フック・入力の送信・通知・トレイのアイコンを実際に動かすプログラムは起動しない
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで直す
- パッケージを追加しない。環境変数を変えない。インターネットから何もダウンロードしない
- 判断が必要な点は、この指示書の「理由」や 0.3 の方針に沿って決め、その箇所に `// 要確認:` を残す

## 10. 完了報告

日本語で、次の形で書く。

1. 変更したファイルと、それぞれの変更内容
2. ビルドとテストの結果（テストの件数と合否）
3. 指示書から外れた点と、その理由（無ければ「無し」）
4. 新しく残した `// 要確認:` の一覧
5. ユーザーが実機で最初に確かめること
