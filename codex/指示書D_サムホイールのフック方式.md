# 指示書D：サムホイールの肩代わりをフック方式にする

## 0. この指示書の目的

### 0.1 実機でわかったこと（11/19、記録ツールのログより）

LogiGap が肩代わりしている間、サムホイールは HID++ で横取りしている（`THUMB_WHEEL` setReporting(1,1)）。このやり方だと、Options+ に引き継いだ直後にサムホイールが動かなくなる。動かなくなったサムホイールは、クリックすると戻る。

| | 横取りあり（16:25） | 横取りなし（16:27、`divert: false`） |
|---|---|---|
| Options+ が設定を始める前に届いた、横取り中の回転通知 | 約 800 件 | 0 件 |
| Options+ の setThumbwheelReporting / setWheelMode | 3 回ずつやり直し | 1 回ずつ |
| 引き継ぎの直後 | カクカクして止まり、クリックで戻る | すぐ正しい向きで動く |

横取りした通知は、レシーバーを開いているすべてのプロセスに届く。そのため、Options+ が自分で横取りを設定する前から、回転の通知を受け取ってしまう。これで Options+ の内部の状態がずれる、と判断した。マウスの最終的な状態は、どちらの場合も同じ（status 1,3）だった。

### 0.2 方針

肩代わりの間も、**マウスの THUMB_WHEEL には一切書き込まない**。サムホイールはマウス本来の横スクロール（`WM_MOUSEHWHEEL`）のまま Windows に届く。それを LogiGap の低レベルマウスフック（`GestureModule` の `WH_MOUSE_LL`）で受け取り、元の入力は止める。代わりに、向きを反転し、速度を `speedPercent` にした横スクロールを `SendInput` で送り直す。これを「橋渡し」と呼ぶ。

- マウスの状態は、0.1 の「横取りなし」と同じになる。Options+ が見る状態も、LogiGap が無いときと同じになる
- 向きと速度は、今までの肩代わり（横取り方式）と同じに保つ
- 今までの横取り方式は、設定で選べるように残す

## 1. 変更してよいファイル

- `app/LogiGap.Core/Settings.cs`
- `app/LogiGap.Core/Protocol.cs`
- `app/LogiGap.Core/GapSession.cs`
- `app/LogiGap/GestureModule.cs`
- `app/LogiGap/Program.cs`
- `app/LogiGap.Tests/` の下（既存テストの修正と、新しいテストファイルの追加）
- `app/README.md`

ほかのファイルは変えない。特に `app/publish/` と `logs/` には触れない（ユーザーの設定は、後で Claude が書き換える）。

## 2. 設定（Settings.cs）

### 2.1 ThumbSettings

- `public bool Divert` を**削除**し、`public string Mode { get; init; } = "hook";` を追加する。JSON では `gapFiller.thumbWheel.mode`
  - `"hook"`：この指示書の橋渡し（既定）
  - `"divert"`：今までの横取り方式（setReporting(1,1) と、通知からの変換）
  - `"off"`：サムホイールには何もしない（今の `divert: false` と同じ）
- `Validate` で、`Mode` がこの 3 つ以外（null を含む）なら `InvalidDataException("thumbWheel.mode は hook / divert / off のどれかです")` を投げる。大文字小文字は区別する
- 古い設定ファイルに残っている `"divert"` キーは、System.Text.Json が無視する。そのため、そのファイルは既定の `"hook"` で動く。これでよい。互換のための読み替えは作らない
- `DirectionSign` のコメントを次の意味に直す：「1 = Options+ の『反転』オンと同じ向き。-1 = その逆」
- `SpeedPercent` と `DirectionSign` の意味と範囲は変えない。両方のモードで同じ値を使う

## 3. GapSession の変更

### 3.1 橋渡しの入口（Protocol.cs と GapSession）

`Protocol.cs` に次の 2 つを追加する。

```csharp
public interface INativeWheelBridge
{
    // 低レベルフックのスレッドから呼ばれる。ロックを取らず、すぐ戻ること。
    // true を返したら、呼び出し側は元の入力を止める。
    bool TryTakeHorizontal(int delta);
}

public sealed class NativeWheelScaler
{
    // 送る量 = 元の量 × speedPercent / 100 × (−directionSign)。端数は次回へ持ち越す。
    public int Convert(int nativeDelta, decimal speedPercent, int directionSign);
    public void Reset();
}
```

- `NativeWheelScaler` は、既存の `ThumbWheelConverter` と同じく `decimal` の端数を持ち越す。整数部は `decimal.Truncate` で取り出す
- 符号を反転する理由：マウス本来の横スクロールは、Options+ の「反転」オンの向きと逆になる（実機で確認済み：`divert: false` の肩代わり中は逆回転になった）。`DirectionSign = 1` のとき、横取り方式と同じ向きにするために `−1` を掛ける。この符号は実機で最終確認するので、`// 要確認:` を残す

`GapSession` は `INativeWheelBridge` を実装する。

### 3.2 橋渡しの状態

GapSession に、次の状態を持たせる。

- `private volatile bool bridgeActive;`：フックのスレッドがロックなしで読む。書き込みは必ず `gate` の中で行う
- `ConcurrentQueue<int>`：フックで受け取った元の量。worker が取り出す
- `NativeWheelScaler`：橋渡し専用の変換器。`gate` の中だけで使う
- 記録用：変換して送った件数、最初の 5 件の元の量、送信失敗を記録したかどうか

### 3.3 TryTakeHorizontal（フックのスレッドから呼ばれる）

```text
if (!bridgeActive || delta == 0) return false;
キューに delta を入れる
Wake を呼ぶ（例外は握りつぶす）
return true;
```

- `gate` は取らない。`gate` を持った worker は HID の書き込みをすることがある。フックをその待ちで止めてはいけない
- 例外を外に出さない。キューに入れる前に例外が出たら false を返す（元の入力を素通しする）

### 3.4 橋渡しを始める

- `Change(SessionState.Active, ...)` のとき、`sessionSettings.ThumbWheel.Mode == "hook"` で、一時停止中でなければ、橋渡しを始める
  - `bridgeActive = true`
  - 変換器の端数・件数・例を初期化する
  - ログ：`サムホイール: フック方式で橋渡しを開始（向き反転・速度 {SpeedPercent}%）`
- すでに橋渡し中なら、何もしない

### 3.5 橋渡しを終える（StopBridge(理由)）

`bridgeActive` が true のときだけ、次を行う。

- `bridgeActive = false` にする
- ログ：`サムホイール: 橋渡しを終了（{理由}） 送信 {件数} 件、元の量の例 [{最初の 5 件をカンマ区切り}]`
- 件数・例・端数を初期化する

橋渡しは、**Active から Retreated に変わっても、すぐには終えない**。終えるのは、次のどれかが起きたときだけにする。

| 理由（ログに書く文） | いつ |
|---|---|
| `Options+ がサムホイールを設定` | `ObserveOptions` が Options+ の THUMB_WHEEL fn2（setThumbwheelReporting）を見たとき。Active から退く瞬間でも、退いた後でもよい |
| `引き継ぎから {CleanupDelaySeconds} 秒` | Retreated のまま `retreatAt + CleanupDelaySeconds` を過ぎたとき |
| `リンク切断` | `Receive` でマウスのリンク切断・機器変更を見たとき |
| `レシーバー切断` | `ReceiverLost` |
| `新しい接続` | `Begin`（新しいリンク、または起動時の確認済みマウス） |
| `一時停止` / `再開` | `SetPaused(true)` / `SetPaused(false)` |

**退いた後も続ける理由**：Options+ の最初の書き込み（ふつうは setSensorDpi）から、サムホイールの横取りの設定までに約 1.3 秒ある（16:27:48.873 → 16:27:50.156）。その間、マウス本来の横スクロールを扱うのは誰もいない。ここで橋渡しを止めると、1.3 秒だけ逆向き・2 倍の速さになる。Options+ がサムホイールを横取りすると、マウス本来の横スクロールは出なくなる。そのため、橋渡しを続けても二重には動かない。

注意：`Invalidate()` は、Options+ に退くときにも呼ばれる。`Invalidate()` の中で橋渡しを終えたり、橋渡しのキューを消したりしてはいけない。

### 3.6 worker（Pump）での送信

`Pump()` の `gate` の中で、**`for` ループに入る前に**、次を行う。HID の応答待ち（`pending`）があっても、ここは必ず通ること。

1. 橋渡し中で、`state == Retreated` かつ `clock.Now >= retreatAt + CleanupDelaySeconds` なら、`StopBridge("引き継ぎから … 秒")`
2. キューをすべて取り出す
   - 一時停止中（`paused`）なら、送らずに捨てる
   - それ以外は、`NativeWheelScaler.Convert(元の量, settings.ThumbWheel.SpeedPercent, settings.ThumbWheel.DirectionSign)` で変換する。0 でなければ `input.HorizontalWheel` で送る
   - **橋渡しを終えた後でも、キューに残っているものは送る**。フックで元の入力をすでに止めているので、送らないとスクロールが消える。速度と方向は、即時反映の `settings` を使う（既存の横取り方式と同じ）
   - `input.HorizontalWheel` の例外は、この場で捕まえる。ログ `サムホイール送信失敗: {メッセージ}` を、橋渡し 1 回につき 1 回だけ書いて、残りの処理を続ける。**`blocked` にしてはいけない**（HID の適用を止めないため）
   - 件数を数え、最初の 5 件の元の量を記録する

`WorkerWaitMilliseconds` は、橋渡し中で Retreated のとき、`retreatAt + CleanupDelaySeconds` までに目を覚ますようにする（今の後片付けの期限と同じ時刻なので、既存の計算に含まれていれば、そのままでよい。`cleanupDone` が true の場合も、橋渡し中なら期限で起きること）。

### 3.7 THUMB_WHEEL への書き込み

- 適用の手順（`applyStep == 6`）で、`Mode != "divert"` なら、今の `!Divert` と同じく何も書かずに適用を終える。`"hook"` と `"off"` では、THUMB_WHEEL に一度も書き込まない
- `"divert"` のときの動き（setReporting(1,1)、読み戻し、通知からの変換、後片付けの THUMB OFF）は今のまま
- `"divert"` 以外のときは `thumbOwned` が立たない。そのため後片付けもサムホイールに触れない。この関係はテストで確かめる

### 3.8 読み戻しの判定を直す（`"divert"` のとき）

今は、読み戻しが `01 01` 以外なら「サムホイール読み戻し不一致: 要確認」と書く。しかし status の 2 バイト目は、bit0 = 反転、bit1 = 指が触れている、である。16:24:33 には、指が触れていたため `01 03` が返り、誤って不一致と書いた。

- `p[0] == 1 && (p[1] & 1) == 1` なら `サムホイール読み戻し OK {p[0]:X2} {p[1]:X2}`
- それ以外は `サムホイール読み戻し不一致 {p[0]:X2} {p[1]:X2}: 要確認`

### 3.9 設定の再読込

`UpdateSettings` では、今の `wheel.Reset()` に加えて、橋渡しの変換器の端数も捨てる。`Mode` は HID の設定と同じく、次の接続から効く（`sessionSettings` を使う）。

## 4. GestureModule の変更（フック）

- コンストラクターの最後に `INativeWheelBridge? wheelBridge = null` を追加し、フィールドに持つ
- `Hook()` の `if (native.Extra != KeyStroke.ExtraInfo)` の中で、今の `suppress = engine.Handle(e.Value)` の**後**に、次を足す

```csharp
// 0x1 = LLMHF_INJECTED, 0x2 = LLMHF_LOWER_IL_INJECTED。ほかのアプリが送った入力には触れない。
if (!suppress && message == 0x20E && (native.Flags & 0x3) == 0 &&
    wheelBridge?.TryTakeHorizontal(unchecked((short)(native.Data >> 16))) == true)
    suppress = true;
```

- ジェスチャーが一時停止中（ユーザーの一時停止、かざぐるマウス動作中、入力ワーカーの異常）でも、この判定は行うこと。橋渡しは指示書 A の機能で、指示書 B の一時停止とは関係ない
- 例外が出たら、今の `catch` と同じく素通しする（suppress しない）
- LogiGap 自身が送った横スクロールは `Extra == ExtraInfo` なので、ここには来ない。この前提はテストでは確かめられないので、README に書く

## 5. Program.cs

`GestureModule` を作るときに、`gap.Session` を `wheelBridge` として渡す。ほかは変えない。

## 6. テスト（xUnit、既存の `Harness` を使う）

### 6.1 既存テストの扱い

- 既存のテストのうち、サムホイールの横取り（setReporting(1,1)、読み戻し、通知の変換、THUMB OFF の後片付け）を前提にしているものは、設定で `Mode = "divert"` を**明示する**形に直す。期待値は変えない
- `ReplayTests` の最後の検証（キー入力・ホイールとも 0 件）は変えない

### 6.2 新しいテスト

`FakeInput` に、`HorizontalWheel` で例外を投げる切り替え（例：`ThrowOnWheel`）を足してよい。

1. 設定：既定は `"hook"`。`"divert"` と `"off"` は通る。`"Hook"`・`""`・null は例外。`"mode": "divert"` の JSON は読める。`"divert": false` だけの古い JSON は `"hook"` になる
2. hook：接続判定で Active になっても、THUMB_WHEEL fn2 への送信が 1 件も無い。`TryTakeHorizontal(120)` は true。`Pump` 後の `WheelEvents` は `[-60]`（50%、`DirectionSign = 1`）。`DirectionSign = -1` なら `[60]`
3. 端数：50% で `TryTakeHorizontal(1)` を 3 回 → `WheelEvents` は `[-1]` だけ
4. Active になる前（接続判定中、Disconnected、「Options+ 設定済み」で Retreated）は false
5. Active → Options+ の setSensorDpi で Retreated：まだ true。続けて Options+ の setThumbwheelReporting の応答が来たら false。ログに `橋渡しを終了（Options+ がサムホイールを設定）`
6. Options+ が THUMB_WHEEL を書かないまま `retreatAt + CleanupDelaySeconds` を過ぎて `Pump` → false。その前の時刻では true。橋渡し中の `WorkerWaitMilliseconds` が、期限までの残り時間以下である
7. リンク切断・`ReceiverLost`・新しい `Begin`・`SetPaused(true)` のそれぞれで false
8. キューの扱い：受け取った後に Options+ のサムホイール設定で終了 → `Pump` で送られる。受け取った後に `SetPaused(true)` → 送られない
9. HID の応答待ち中（`AutoReply = false` で適用の途中に止める）でも、`TryTakeHorizontal` の分は `Pump` で送られる
10. 送信失敗：`ThrowOnWheel` で例外 → ログ `サムホイール送信失敗` が 1 行だけ。続く HID の適用（`AutoReply` を戻した後）が止まらない
11. off：THUMB_WHEEL への書き込みが無く、`TryTakeHorizontal` は false
12. divert：読み戻しが `01 03` なら OK のログ、`01 00` なら不一致のログ
13. hook と off では `thumbOwned` が立たないので、後片付けで THUMB_WHEEL の照会・書き込みが無い

## 7. README

`app/README.md` の設定の説明に、次を書く。

- `gapFiller.thumbWheel.mode`：`hook`（既定）/ `divert` / `off` の意味
- hook を既定にした理由：横取り方式だと、Options+ に引き継いだ直後にサムホイールが止まる（0.1 の表を短く）
- hook の制限
  - 橋渡し中は、ほかの機器の横スクロールも同じく反転・減速される。ほかのアプリが送った入力（injected）は対象外。橋渡しは、肩代わりの開始から「Options+ がサムホイールを設定する」まで、長くても引き継ぎ後 `cleanupDelaySeconds` 秒まで
  - LogiGap のマウスフックが外れている間は、マウス本来の向き・速さになる
- `divert` キーは廃止したこと。古い設定に残っていても無視され、`hook` で動く
- ログの見方：`橋渡しを開始` / `橋渡しを終了（理由） 送信 n 件、元の量の例 [...]`

## 8. 守ること

- `app/` の外は変えない（`logs/` は読むだけ。`app/publish/` には触れない）
- アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しない
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで直す
- パッケージを追加しない。環境変数を変えない
- 判断が必要な点は、この指示書の「理由」に沿って決め、その箇所に `// 要確認:` を残す

## 9. 完了報告

日本語で、次の形で書く。

1. 変更したファイルと、それぞれの変更内容
2. ビルドとテストの結果（テストの件数と合否）
3. 指示書から外れた点と、その理由（無ければ「無し」）
4. 新しく残した `// 要確認:` の一覧
5. ユーザーが実機で最初に確かめること
