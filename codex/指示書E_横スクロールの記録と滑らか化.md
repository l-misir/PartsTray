# 指示書E：横スクロールの記録と、肩代わり中の滑らか化

## 0. この指示書の目的

### 0.1 実機でわかったこと（11/19 17:10 の試験と、記録ツールのログより）

指示書D（フック方式）で、引き継ぎのときにサムホイールが止まる問題は直った。ただし、肩代わり中のスクロールは、Options+ に切り替わった後と比べて、明らかに速さと滑らかさが違うとユーザーが報告している。

| | 肩代わり中（LogiGap、フック方式） | Options+ に切り替わった後 |
|---|---|---|
| マウスから届く回転の細かさ | 1 回転あたり 20 段（`nativeRes=20`）。1 段は横スクロール 120（ログの「元の量の例」はすべて ±120） | 1 回転あたり 120 単位（`divertedRes=120`）。本来の 1 段 = 6 単位 |
| 回転の通知の間隔（記録ツール、約 15,000 件） | 未測定 | 4〜16ms がほとんど。1 回の通知は 1〜4 単位がほとんど |
| 画面に送る横スクロール | 1 段ごとに 1 回、量 60（50%）をまとめて送る | 未測定（1 回の量、回数、SendInput かどうか、すべて不明） |

肩代わり中は、本来の 1 段（Options+ の 6 単位ぶん）を 1 回でまとめて送るので、Options+ より飛び飛びに動く。速さは、Options+ が実際に送っている量がわからないので、まだ合わせられない。

### 0.2 方針

1. **記録**：マウスフックが見たすべての横スクロール（WM_MOUSEHWHEEL）を、設定で有効にしたときだけ専用のファイルに記録する。LogiGap 自身が送ったもの、ほかのアプリが送ったもの（Options+ を含む）、機器から来たものを区別する。Claude がこの記録と記録ツールのログを照らし合わせ、Options+ の 1 単位あたりの量と、送る間隔を求める
2. **滑らか化**：肩代わり中の 1 段ぶんの量を、すぐに全部は送らず、短い間隔（既定 8ms）で数回（既定 40ms の間に 5 回）に分けて送る。1 回目はすぐに送るので、動き出しは遅れない。間隔と時間は設定で変えられるようにし、記録を測った後に Claude が設定だけで合わせる
3. **精度の高い待ち**：Windows の既定のタイマーは約 15.6ms 刻みなので、8ms 間隔の送信ができない。HID の worker の待ちに、高精度の待機タイマー（`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`）を使う

速さ（`speedPercent`）の仕組みは変えない。測定後に、Claude が設定の値を変える。

## 1. 変更してよいファイル

- `app/LogiGap.Core/Settings.cs`
- `app/LogiGap.Core/Protocol.cs`
- `app/LogiGap.Core/GapSession.cs`
- 新規 `app/LogiGap.Core/WheelTrace.cs`（記録の形式。純粋な処理だけ）
- `app/LogiGap/GestureModule.cs`
- `app/LogiGap/GapModule.cs`
- 新規 `app/LogiGap/WheelTraceWriter.cs`（記録のファイル書き込み）
- `app/LogiGap.Tests/` の下（既存テストの修正と、新しいテストファイルの追加）
- `app/README.md`

ほかのファイルは変えない。特に `app/publish/` と `logs/` には触れない（ユーザーの設定は、後で Claude が書き換える）。

## 2. 設定（Settings.cs）

### 2.1 ThumbSettings に 2 つ足す

```csharp
// 肩代わり中（hook）の 1 段ぶんを、この時間（ms）の間に分けて送る。0 = 分けずにすぐ全部送る（指示書D と同じ動き）。
public int SmoothMs { get; init; } = 40;
// 分けて送る間隔（ms）。
public int SmoothIntervalMs { get; init; } = 8;
```

JSON では `smoothMs` / `smoothIntervalMs`（既存の CamelCase の設定どおり）。

`Validate` に足す。条件に合わないときは `InvalidDataException("thumbWheel.smoothMs は 0〜200、smoothIntervalMs は 4〜50 で、smoothMs が 0 でなければ smoothIntervalMs 以上です")`。

- `SmoothMs` は 0〜200
- `SmoothIntervalMs` は 4〜50
- `SmoothMs` が 0 でないときは `SmoothMs >= SmoothIntervalMs`

**理由**：既定の 40ms / 8ms は、Options+ に切り替わった後の回転の通知（4〜16ms 間隔、1 回 1〜4 単位）に近い細かさ。本来の 1 段（6 単位ぶん）を 5 回に分けると、1 回は約 1.2 単位ぶんになる。

### 2.2 記録の設定（新しい最上位の項目）

```csharp
public sealed record DiagnosticsSettings
{
    // true のとき、マウスフックが見た横スクロールをすべて logs/wheel_yyyyMMdd.log に記録する。
    public bool WheelTrace { get; init; } = false;
}
```

`AppSettings` に `public DiagnosticsSettings Diagnostics { get; init; } = new();` を足す。JSON では `"diagnostics": { "wheelTrace": false }`。`Validate` では、`Diagnostics` が null なら `InvalidDataException("diagnostics が必要です")`。

`TrayContext` の、設定エラー時の安全な設定（`Program.cs`）は変えなくてよい（`Diagnostics` は既定の false になる）。

## 3. 滑らか化（Protocol.cs に `WheelSmoother` を追加）

### 3.1 クラス

時刻は引数で受け取り、スレッドもタイマーも持たない純粋なクラスにする（テストで時刻を進めて確かめるため）。

```csharp
public sealed class WheelSmoother
{
    public void Configure(int smoothMs, int intervalMs);   // 変更時は Reset も行う
    public void Add(int amount, TimeSpan now);
    public int Take(TimeSpan now);                         // 送る量。送るものが無ければ 0
    public TimeSpan? NextDue { get; }                      // 次に Take すべき時刻。残りが無ければ null
    public void Reset();                                   // 残りを捨てる
}
```

- 分割数 `slices = ceil(smoothMs / intervalMs)`（40 / 8 なら 5）
- 状態は `pending`（int、まだ送っていない量）、`slicesLeft`、`nextDue`

### 3.2 Add(amount, now)

- `amount == 0` なら何もしない
- 残りが無い（`pending == 0`）とき：`pending = amount`、`slicesLeft = slices`、`nextDue = now`（**1 回目はすぐ送る**）
- 残りと同じ向きのとき：`pending += amount`、`slicesLeft = slices`（残りと新しい分を、ここから `slices` 回で送り直す）。`nextDue` は変えない
- 残りと逆向きのとき：前の残りは捨てる。`pending = amount`、`slicesLeft = slices`、`nextDue = now`

**理由**：逆向きに回したときに、前の向きの残りが流れ続けると、向きを変えたのに一瞬逆に動く。Options+ もそうはならない。

### 3.3 Take(now)

- `pending == 0` または `now < nextDue` なら 0 を返す
- 送る量：`out = sign(pending) * ceil(|pending| / slicesLeft)`（整数で `(abs + slicesLeft - 1) / slicesLeft`）
- `pending -= out`、`slicesLeft--`
- `pending == 0` になったら、`slicesLeft = 0` にして終わり（`NextDue` は null）
- まだ残るなら `nextDue += interval`。ただし、それでも `now` 以下なら `nextDue = now + interval`（遅れたときに、まとめて送らない）
- 1 回の `Take` で返すのは 1 回分だけ

例（50%、本来の 1 段 → 量 60、40 / 8）：0ms・8ms・16ms・24ms・32ms に 12 ずつ。合計は分けないときと同じ 60。

量が小さいとき（例：3 を 5 回に）：`1, 1, 1` で終わる（切り上げなので早めに送り切る）。

## 4. GapSession の変更

### 4.1 持ち物

- `private readonly WheelSmoother smoother = new();`
- 橋渡しの集計：受け取った件数と合計（元の量）、送った件数と合計（送った量）

### 4.2 設定の反映

- コンストラクタと `UpdateSettings` で `smoother.Configure(settings.ThumbWheel.SmoothMs, settings.ThumbWheel.SmoothIntervalMs)`
- `UpdateSettings` では、既存の `nativeScaler.Reset()` と同じく、滑らか化の残りも捨てる

### 4.3 Pump

既存の、キューを取り出して送る部分（`while (nativeWheel.TryDequeue(...))`）を次のように変える。場所は今と同じ（`for` の前）。

1. キューを取り出す。一時停止中は捨てる（今と同じ）
2. 量を `nativeScaler.Convert` で変える（今と同じ）。0 なら次へ
3. 受け取りの集計に足す。元の量の例（最初の 5 件）は、ここで記録する
4. `SmoothMs == 0` なら、今と同じくすぐに `input.HorizontalWheel(量)` を送る
5. `SmoothMs > 0` なら、`smoother.Add(量, clock.Now)`
6. キューを取り出し終えたら、一時停止中でなければ `int slice = smoother.Take(clock.Now)`。0 でなければ `input.HorizontalWheel(slice)` を送る
7. 送信の例外は、今と同じく 1 回だけ記録し、`blocked` にしない。例外のときも、`Take` で減らした分は戻さない（送り直さない）
8. 送った件数と合計は、実際に送れた分だけ数える

### 4.4 橋渡しの終了（StopBridge）

- ログを次の形に変える：

  `サムホイール: 橋渡しを終了（{理由}） 受け取り {n} 件（合計 {元の量の合計}）、送信 {m} 件（合計 {送った量の合計}）、元の量の例 [...]`

- 終了の後も、滑らか化の残りは最後まで送る（指示書D の「終了後の残りも送る」と同じ考え方）。ただし `SetPaused(true)` では残りを捨てる（`smoother.Reset()`）
- 集計は、終了のログを書いた後に 0 に戻す。終了後に送った残りは集計しない

### 4.5 WorkerWaitMilliseconds

一時停止中でなく、`smoother.NextDue` があるときは、その時刻も「次に起きる時刻」の候補に入れる（ほかの候補と早いほうを使う）。既存の計算（切り上げ、最小 1ms）はそのまま。

**理由**：滑らか化の 2 回目以降は、新しい入力が無くても決まった時刻に送る必要がある。

### 4.6 変えないこと

- divert モードの変換（`ThumbWheelConverter`）には滑らか化を入れない。divert は元から細かいので
- 橋渡しの開始・終了の条件は、指示書D のまま

## 5. GapModule の worker の待ち（高精度タイマー）

### 5.1 タイマー

`GapModule.cs` の中に、高精度の待機タイマーを `WaitHandle` として包むクラスを作る（`WaitHandle` を継承し、`SafeWaitHandle` に設定する）。

```csharp
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, in long dueTime, int period, nint completion, nint argument, nint wakeContext, uint tolerableDelay);
// flags: CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002
// access: TIMER_ALL_ACCESS = 0x001F0003
```

- 作れなかったとき（無効なハンドル）は、`null` にしておき、アプリのログに 1 回だけ `高精度タイマーを使えません: 横スクロールの分割送信は約 16ms 間隔になります` と書く。アプリは止めない
- `dueTime` は相対時間で、`-(ms * 10_000)`（100ns 単位、負の値）

### 5.2 待ち方

worker のループを次のように変える。

```
int ms = Session.WorkerWaitMilliseconds;
if (ms != Timeout.Infinite && ms <= 30 && 高精度タイマーがある && SetWaitableTimerEx が成功)
    WaitHandle.WaitAny([wake, timer]);
else
    wake.WaitOne(ms);
```

- 30ms を超える待ちと無期限の待ちは、今までどおり `wake.WaitOne`
- `SetWaitableTimerEx` が失敗したら、その回は `wake.WaitOne(ms)` にする（ログは最初の 1 回だけ）
- `Dispose` でタイマーを閉じる

## 6. 横スクロールの記録

### 6.1 形式（Core、`WheelTrace.cs`）

純粋な関数にする（テストするため）。

```csharp
public enum WheelTraceSource { LogiGap, Injected, Device }
public enum WheelTraceTaken { None, Gesture, Bridge }
public readonly record struct WheelTraceEntry(long Timestamp, int Delta, uint Flags, nuint Extra, WheelTraceTaken Taken);
public static class WheelTrace
{
    public static WheelTraceSource Source(uint flags, nuint extra);
    public static string Header(DateTime start);
    public static string Format(WheelTraceEntry entry, DateTime wallClock, double sincePreviousMs);
}
```

- `Source`：`extra == KeyStroke.ExtraInfo` なら `LogiGap`、そうでなく `(flags & 0x3) != 0` なら `Injected`、それ以外は `Device`
- `Header`：`# 横スクロール記録 開始 yyyy-MM-dd HH:mm:ss.fff 列: 時刻 前からの間隔ms 量 送り元 flags extra 横取り`
- `Format`：1 行 1 件。区切りは半角空白。例

  `17:10:41.234 +8.1 -120 Device flags=00 extra=0 taken=Bridge`

  - 時刻は `HH:mm:ss.fff`
  - 間隔は小数 1 桁（`InvariantCulture`）。最初の 1 件は `+0.0`
  - flags は 16 進 2 桁、extra は 16 進（桁埋めなし）
  - 横取りは `None` / `Gesture` / `Bridge`

### 6.2 フックでの記録（GestureModule.Hook）

- 記録するのは `message == 0x20E` のときだけ
- **Extra の判定（`native.Extra != KeyStroke.ExtraInfo`）より外側**で記録する。LogiGap 自身が送った横スクロールも記録するため
- `Taken`：`engine.Handle` が抑止したら `Gesture`、`TryTakeHorizontal` で抑止したら `Bridge`、どちらでもなければ `None`
- `Timestamp` は、`Hook` の先頭で取っている `Stopwatch.GetTimestamp()` の値をそのまま使う
- フックのスレッドでは、記録を `ConcurrentQueue` に入れるだけにする。ファイルには書かない。ロックを取らない
- 記録が無効のとき（設定が false）は、何もしない（`volatile` などのフラグを 1 回読むだけ）
- キューにたまった件数が 20,000 を超えたら、それ以上は入れずに捨てた件数を数える
- 記録の処理で例外が出ても、入力の判定（`suppress`）には影響させない

### 6.3 ファイルへの書き込み（`WheelTraceWriter.cs`）

- 専用の背景スレッド（`IsBackground = true`）が、約 250ms ごとにキューを取り出してファイルに追記する
- ファイル：`AppContext.BaseDirectory` の `logs/wheel_yyyyMMdd.log`（UTF-8、`FileShare.ReadWrite`、追記）。日付は書き込むときの日付
- 時刻の換算：記録を開始したときに `DateTime.Now` と `Stopwatch.GetTimestamp()` の組を取り、`開始時刻 + (Timestamp - 開始の値) / Stopwatch.Frequency` で求める
- 記録の開始ごとに `Header` を 1 行書く
- 捨てた件数があれば、次の書き込みで `# 混雑: {n} 件を捨てた` を 1 行書く
- 1 回の起動で 200,000 件を書いたら、記録を止めて、アプリのログに `横スクロール記録: 上限 200000 件に達したため停止` と書く
- ファイルに書けないときの例外は、アプリのログに 1 回だけ書き、アプリは止めない

### 6.4 開始と終了

- 起動時に `settings.Diagnostics.WheelTrace` が true なら開始する。`SettingsChanged` で false → true になったら開始、true → false になったら残りを書いて止める
- アプリのログに次を書く
  - 開始：`横スクロール記録: 開始（logs/wheel_yyyyMMdd.log）`
  - 終了：`横スクロール記録: 終了 {n} 件`
- `Dispose` では、記録を止めて残りを書く

## 7. テスト（xUnit、既存の `Harness` を使う）

### 7.1 既存テストの扱い

- 滑らか化の既定値（40ms）で期待値が変わる既存テスト（特に `NativeWheelTests`）は、設定で `SmoothMs = 0` を明示する形に直す。期待値は変えない。`NativeWheelTests` の `Config()` に `int smoothMs = 0` の引数を足す形でよい
- `ReplayTests` の最後の検証（キー入力・ホイールとも 0 件）は変えない

### 7.2 新しいテスト

1. 設定：`smoothMs` / `smoothIntervalMs` の既定は 40 / 8。JSON で読めて、保存すると出力される。`smoothMs` が -1・201、`smoothIntervalMs` が 3・51、`smoothMs = 4` と `smoothIntervalMs = 8` の組は例外。`smoothMs = 0` と `smoothIntervalMs = 8` は通る
2. 設定：`diagnostics.wheelTrace` の既定は false。`{"diagnostics":{"wheelTrace":true}}` を読める。`Diagnostics = null` は例外
3. `WheelSmoother`：40 / 8 で `Add(60, 0ms)` → 0・8・16・24・32ms の `Take` が 12 ずつ。途中の時刻（例：4ms）の `Take` は 0。最後の後は `NextDue` が null
4. `WheelSmoother`：負の量も同じ（`-60` → `-12` が 5 回）
5. `WheelSmoother`：小さい量（`Add(3)`）→ `1, 1, 1` で終わる
6. `WheelSmoother`：同じ向きの追加。`Add(60, 0)`、0ms と 8ms で 2 回 `Take`（12, 12）、`Add(60, 10ms)` → 残り 96 を 5 回に分けて送り直す。合計は 120
7. `WheelSmoother`：逆向き。`Add(60, 0)`、0ms で `Take`（12）、`Add(-60, 3ms)` → 3ms の `Take` が `-12`。その後の合計は -60 で、前の向きの残り（48）は送られない
8. `WheelSmoother`：遅れたとき。`Add(60, 0)`、0ms で `Take`、次を 50ms で `Take` → 1 回分（12）だけ返り、`NextDue` は 58ms
9. `WheelSmoother`：`Reset` と `Configure` で残りが消える
10. GapSession（hook、50%、`DirectionSign = 1`、40 / 8）：Active で `TryTakeHorizontal(120)` → `Pump` で `WheelEvents` は `[-12]`。`WorkerWaitMilliseconds` は 8 以下。時刻を 8ms ずつ進めて `Pump` → 最後は `[-12, -12, -12, -12, -12]`
11. GapSession：`SmoothMs = 0` なら、今までどおり `[-60]`
12. GapSession：滑らか化の途中で Options+ がサムホイールを設定（橋渡しの終了）→ 残りは送られる。終了のログに `受け取り 1 件（合計 120）、送信` が含まれる
13. GapSession：滑らか化の途中で `SetPaused(true)` → 残りは送られない。`WorkerWaitMilliseconds` が滑らか化のために短くならない
14. GapSession：送信失敗（`ThrowOnWheel`）でも、残りの `Take` は進み、ログ `サムホイール送信失敗` は 1 行だけ
15. `WheelTrace.Source`：`extra = KeyStroke.ExtraInfo` → `LogiGap`（flags に関係なく）。flags 0x1 → `Injected`。flags 0x2 → `Injected`。flags 0 → `Device`
16. `WheelTrace.Format`：例の行と同じ形になる（時刻、`+8.1`、量、送り元、`flags=01`、`extra=4C474150`、`taken=Bridge`）。文化（カルチャ）に左右されない

## 8. README

`app/README.md` の指示書D の説明の後に、次を書く。

- `thumbWheel.smoothMs` / `smoothIntervalMs` の意味と既定値。0 にすると分けずに送ること
- 分ける理由：マウス本来の横スクロールは 1 回転 20 段と粗く、Options+ は 120 単位で受け取っているため（0.1 の表を短く）
- `diagnostics.wheelTrace`：記録の場所（`logs/wheel_yyyyMMdd.log`）、列の意味、送り元の区別（LogiGap / Injected / Device）、上限 200,000 件。ふだんは false にしておくこと
- 終了のログの新しい形（受け取り・送信の件数と合計）

## 9. 守ること

- `app/` の外は変えない（`logs/` は読むだけ。`app/publish/` には触れない）
- アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しない
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで直す
- パッケージを追加しない。環境変数を変えない
- 判断が必要な点は、この指示書の「理由」に沿って決め、その箇所に `// 要確認:` を残す

## 10. 完了報告

日本語で、次の形で書く。

1. 変更したファイルと、それぞれの変更内容
2. ビルドとテストの結果（テストの件数と合否）
3. 指示書から外れた点と、その理由（無ければ「無し」）
4. 新しく残した `// 要確認:` の一覧
5. ユーザーが実機で最初に確かめること
