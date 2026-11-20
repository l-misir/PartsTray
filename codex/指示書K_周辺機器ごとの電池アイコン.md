# 指示書K：周辺機器ごとに、通知領域に電池残量のアイコンを出す

## 0. この指示書の目的

### 0.1 ユーザーの要望

周辺機器の電池残量を、**1 台につきアイコン 1 つ**で通知領域に出したい。対象は次のとおり。

| 機器 | つながり方 | 読み方 |
|---|---|---|
| MX Master 4（マウス、WPID B042） | Logi Bolt レシーバーの番号 2 | HID++ 2.0 の UNIFIED_BATTERY（0x1004） |
| MX Keys S（キーボード、WPID B378） | 同じレシーバーの番号 3 | 同上 |
| WH-1000XM6（ヘッドホン） | Bluetooth | Windows の機器のプロパティ |

ほかのロジクール機器・Bluetooth 機器も、同じ仕組みで読めれば出す。

### 0.2 実機で確かめたこと

- 記録ツールのログ：マウスは `UNIFIED_BATTERY` が機能番号 9、キーボードは 8。マウスから自発的に来る通知（swid=0、fn0）の中身は `55 08 00 00`（85%・レベル bit 0x08）。Options+ の getStatus（fn1）の応答も同じ形
- WH-1000XM6：`Get-PnpDeviceProperty` で、`WH-1000XM6 Hands-Free AG`（Class=System）の機器に、キー `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` の値 **56**（%）があった。ほかの WH-1000XM6 の機器（MEDIA、Bluetooth、Avrcp Transport）にはこの値が無かった
- 参考：同じことをする GPL-3.0 のアプリ「BatteryHub」（krisgfx9/Wireless-Device-Battery-Monitor）がある。**コードは写さない**（ライセンスが違うため）

### 0.3 方針

- ロジクールの電池は、LogiGap がすでに開いているレシーバーの HID++ の口を使う。**読み取りだけ**を、**swid 7** で送る（LogiGap のつなぎ役は 5、Options+ は 0xA・0xB・0xC・0xE を確認済み）
- 電池の状態の管理（どの機器に何を聞いたか、応答の解釈）は `LogiGap.Core` の純粋なクラスにして、テストできるようにする
- つなぎ役（`GapSession`）の動きは変えない。電池の要求と応答が、つなぎ役の判断（Options+ の検出など）に影響しないこと
- 機器が眠って切れても、アイコンは消さずに最後の値を薄く表示する（出たり消えたりすると、通知領域の並びが崩れて使いにくいため）

**作業の順番**：本体のコード（§2〜§6）を先に書き、ビルドが通ってからテスト（§7）と README（§8）を書く。

## 1. 変更してよいファイル

- `app/LogiGap.Core/Settings.cs`
- `app/LogiGap.Core/Protocol.cs`（`HidReport.Request` に swid を渡せるようにするだけ。既定は 5 のまま）
- 新規 `app/LogiGap.Core/Battery.cs`（純粋な処理。名前は変えてよい）
- `app/LogiGap/GapModule.cs`（受信の分配と、送信の排他）
- 新規 `app/LogiGap/BatteryModule.cs`（Bluetooth の読み取り、描画、アイコン。名前は変えてよい）
- `app/LogiGap/Program.cs`（モジュールの登録）
- 指示書J で作った数字のフォントのコード（Core）：絵文字（機器の形）を足すだけ
- `app/LogiGap.Tests/` の下（新しいテストの追加）
- `app/README.md`

ほかのファイルは変えない。`app/publish/` と `logs/` には触れない。

## 2. 設定（Settings.cs）

`AppSettings` に最上位の `Battery` を足す。

```csharp
public sealed record BatterySettings
{
    public bool Enabled { get; init; } = true;
    public bool Logitech { get; init; } = true;
    public bool Bluetooth { get; init; } = true;
    public int LowWarn { get; init; } = 20;          // 以下で橙（%）
    public int LowHot { get; init; } = 10;           // 以下で赤（%）
    public List<string> Hidden { get; init; } = [];  // 表示しない機器の名前（完全一致）
    public int RefreshMinutes { get; init; } = 10;   // ロジクール機器に読み直しを送る間隔
    public int BluetoothPollSeconds { get; init; } = 60;
}
```

`Validate`：`Battery`・`Hidden` が null でない。`LowHot` < `LowWarn`、どちらも 1〜100。`RefreshMinutes` 1〜120。`BluetoothPollSeconds` 10〜600。

## 3. ロジクール機器の電池（Core：`LogitechBatteryTracker`）

### 3.1 送ってよい要求（これ以外は送らない。送る前に検査して、違えば例外）

すべて swid 7、長い形式（0x11）。

| 機能 | 関数 | 意味 |
|---|---|---|
| ROOT（番号 0） | fn0 | getFeature（引数は機能 ID 2 バイト）。0x0005・0x1004・0x1000 だけ |
| DEVICE_NAME（0x0005） | fn0 / fn1 / fn2 | 名前の長さ / 名前（引数は文字の位置）/ 機器の種類 |
| UNIFIED_BATTERY（0x1004） | fn1 | getStatus |
| BATTERY_STATUS（0x1000、0x1004 が無い古い機器だけ） | fn0 | getBatteryLevelStatus |

**書き込みの関数は一つも送らない。**

### 3.2 流れ

- 入力：レシーバーから届いたすべての報告（`HidReport`）、時刻。出力：送る報告
- 機器の番号 1〜6 ごとに状態を持つ
- **リンク確立**（`ReportKind.Connection` で `LinkUp`）を見たら、その番号を「つながっている」にして、まだ知らなければ次を順に聞く：getFeature(0x1004) → 無ければ getFeature(0x1000) → getFeature(0x0005) → 名前（fn0 で長さ、fn1 を必要な回数）→ 種類（fn2）→ 電池（getStatus）。知っていれば電池だけ聞く
- **リンク切断**を見たら「つながっていない」にする。値は消さない
- レシーバーを開き直したとき（GapModule の `ReceiverOpened` / `ReceiverLost` に合わせる）は、全部を「つながっていない」にする。既存のマウスは接続通知が来ないことがあるので、開き直した直後に番号 1〜6 に ROOT の ping（fn1）を送り、応答があった番号をリンク確立と同じに扱う（ping は ROOT fn1 なので、3.1 の表に足してよい）
- 1 度に送る要求は 1 つだけ。応答（同じ番号・機能・関数・swid 7）が来たら次へ。1 秒で応答が無ければ 2 回まで送り直し、それでも無ければその番号の問い合わせを止め、次のリンク確立まで待つ
- エラーの応答（`Error10`・`Error20`）は、その質問を飛ばして次へ（機能が無い、など）
- **電池の通知**：swid 0 で、その機器の UNIFIED_BATTERY の fn0、または BATTERY_STATUS の fn0 が来たら、値を更新する（Options+ が聞いた応答は使わない。swid が 7 でないものは無視）
- `RefreshMinutes` ごとに、つながっている機器へ電池の getStatus を送る

### 3.3 値の解釈

- UNIFIED_BATTERY（getStatus の応答と通知）：`[0]` 残量 %（0〜100）、`[1]` レベルのビット（1=危険・2=低・4=良・8=満）、`[2]` 充電の状態（0=放電中、1=充電中、2=ゆっくり充電中、3=充電完了、4=エラー）、`[3]` 外部電源。`[0]` が 0 で `[1]` だけがあるときは、レベルから概算（危険 5%・低 20%・良 50%・満 90%）し、「概算」の印を付ける
- BATTERY_STATUS：`[0]` 残量 %、`[2]` 状態（0=放電中、1〜2=充電中、3=充電完了、それ以外=不明）
- 種類（DEVICE_NAME fn2）：0=キーボード、3=マウス、それ以外=その他（`// 要確認:` を残してよい）

### 3.4 つなぎ役との関係

- GapModule の受信処理で、`Session.Receive(report)` の**後に**電池の追跡にも同じ報告を渡す（つなぎ役の処理を先にして、その時間を変えない）
- 送信は同じ `Transport` を使う。`Transport.Send` に `lock` を足し、つなぎ役の送信と同時に書き込まないようにする
- 電池の処理は、つなぎ役の `gate` を取らない。自分のロックだけを使う
- 電池の要求の時間切れ・定期の読み直しは、電池用の `System.Threading.Timer`（1 秒ごと）で進める

**理由**：swid 7 の応答は、つなぎ役から見ると「ほかのソフトの応答」になる。つなぎ役が Options+ と見なすのは、setCidReporting・setThumbwheelReporting・setSensorDpi の応答だけなので、電池の読み取りでは引き継ぎは起きない。テストでこれを確かめる（§7）。

## 4. Bluetooth 機器の電池（app）

- `SetupDiGetClassDevsW(null, null, 0, DIGCF_PRESENT | DIGCF_ALLCLASSES)` で機器を並べ、各機器で `SetupDiGetDevicePropertyW` を使ってキー `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}`、PID 2 を読む（型は `DEVPROP_TYPE_BYTE`）。値があった機器だけを対象にする
- 名前：`DEVPKEY_Device_FriendlyName`。末尾の ` Hands-Free AG`・` Hands-Free`・` Avrcp Transport` を取る（Core の純粋な関数）
- つながっているか：`CM_Get_DevNode_Status` で `DN_STARTED` が立っていれば、つながっている。立っていなければ、つながっていない（最後の値を薄く表示）。`// 要確認:` を残す（切断時の実機の様子は未確認）
- 種類はヘッドホン
- `BluetoothPollSeconds` ごとに読み直す。読み取りは UI スレッドではなく、背景で行う

## 5. アイコン（app）

### 5.1 1 台 1 アイコン

- 電池の値が一度でも取れた機器ごとに、`NotifyIcon` を 1 つ作る。右クリックで `TrayContext` のメニューを出す
- `Hidden` にある名前の機器は作らない。設定の再読込で消す・出す
- アイコンの順番は、作った順（Windows が覚えるため、途中で作り直さない）

### 5.2 絵（大きさ・色・フォントは指示書J と同じ決め方）

- 上の段：機器の形の小さな絵（マウス・キーボード・ヘッドホン・その他）。指示書J の数字と同じく、ドットの並びで Core に持つ。高さは数字と同じ選び方
- 下の段：残量の数字（%）。値が無ければ `--`。100 は指示書J の 3 桁の扱いに合わせる
- 色：`LowWarn` 以下で橙、`LowHot` 以下で赤、充電中は緑（#34C759）、それ以外はテーマの色
- つながっていないとき：全体を半透明（アルファ 40%）にする
- 値が「概算」のときは、数字の後に何も付けず、ツールチップにだけ「（概算）」と書く

### 5.3 ツールチップ

`MX Master 4 85%（充電中）`、つながっていなければ `MX Master 4 85%（切断中・22:31 時点）`。

### 5.4 更新と終了

- 値・つながり・テーマが変わったときだけ描き直す（電池は頻繁に変わらない）
- `Dispose` で、アイコンを消して破棄し、ハンドルを `DestroyIcon` する

## 6. Program.cs

`BatteryModule` を作り、`modules` に足す。ロジクールの追跡は `GapModule` の中にあるので、`GapModule` から値の変化のイベントを出し、`BatteryModule` がそれを受けて描く形にする（スレッドをまたぐので、UI スレッドへ投函する）。

## 7. テスト（本体のコードが通ってから）

Core の `LogitechBatteryTracker` を、偽の送信先と時計で確かめる。

1. リンク確立 → getFeature(0x1004) → getFeature(0x0005) → 名前 → 種類 → getStatus の順に、1 つずつ送る。すべて swid 7・読み取りだけ
2. getStatus の応答 `55 08 00 00` → 85%、レベル満、放電中
3. 通知（swid 0、UNIFIED_BATTERY fn0）`14 02 01 00` → 20%、充電中
4. swid 0xB の getStatus の応答は無視する
5. 0x1004 が無い（getFeature の応答の番号が 0）→ 0x1000 を聞き、`[0]=40、[2]=0` → 40%
6. エラーの応答 → 飛ばして次の質問へ
7. 1 秒で応答が無い → 2 回まで送り直し、その後は止まる。次のリンク確立で再開する
8. リンク切断 → つながっていないが、値は残る
9. `RefreshMinutes` 後に getStatus をもう一度送る
10. 送る報告を検査する関数が、書き込みの関数（例：setCidReporting）を拒否する
11. つなぎ役への影響：`Harness` で Active にした後、swid 7 の getFeature・getStatus・名前の応答を `Session.Receive` に入れても、状態は Active のまま
12. Bluetooth の名前の整え方：`WH-1000XM6 Hands-Free AG` → `WH-1000XM6`
13. 設定：既定値、JSON の読み書き、範囲外の値の例外

## 8. README

指示書J の節の後に、指示書K の節を足す。

- 0.1 と 0.2 の表
- 安全のための決まり：swid 7・読み取りだけ・つなぎ役の判断に影響しないこと
- `battery` の設定の意味（`hidden` で特定の機器を出さない方法を含める）
- アイコンの見方（形・数字・色・薄い表示）
- Windows 11 は新しいアイコンを最初は「隠れたアイコン」に入れるので、表示をオンにすること

## 9. 守ること

- `app/` の外は変えない（`logs/` は読むだけ。`app/publish/` には触れない）
- アプリ・HID・フック・入力の送信・通知・トレイのアイコンを実際に動かすプログラムは起動しない
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで直す
- パッケージを追加しない。環境変数を変えない。インターネットから何もダウンロードしない
- 判断が必要な点は、この指示書の方針に沿って決め、その箇所に `// 要確認:` を残す

## 10. 完了報告

日本語で、次の形で書く。

1. 変更したファイルと、それぞれの変更内容
2. ビルドとテストの結果（テストの件数と合否）
3. 指示書から外れた点と、その理由（無ければ「無し」）
4. 新しく残した `// 要確認:` の一覧
5. ユーザーが実機で最初に確かめること
