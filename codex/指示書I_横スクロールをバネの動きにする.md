# 指示書I：肩代わり中の横スクロールを、Options+ に近い「バネ」の動きにする

## 0. この指示書の目的

### 0.1 実機でわかったこと（11/19 20:18〜20:25、Google スプレッドシート、列幅は一律 100）

| | 1 回あたりの回転 | 進んだ列 |
|---|---|---|
| 肩代わり中（LogiGap、`speedPercent` 50） | 0.90〜1.05 回転 | A → J〜K |
| Options+ | 0.91〜1.00 回転 | A → S〜U |
| 肩代わり中（`speedPercent` 100 に変えた後） | 約 1 回転 | 「ほんのちょっと少ないくらいで、ほぼ大丈夫」（ユーザー） |

量はほぼ合った。ただし、動き方が違う。ユーザーの言葉では、Options+ は次のとおり。

- 1 回のスクロール量が多いと、速くスクロールする（スクロールする量そのものは変わらない）
- 動き始めと終わりの方で減速している

今の LogiGap の滑らか化（指示書E の `WheelSmoother`）は、1 段ぶんを 40ms の間に均等に分けて送るだけ。動き始めはいきなり最高速で、止めるときも一定の速さのまま急に止まる。

### 0.2 方針

1. **バネの動き（`spring`）を足す**：送った量の合計（位置）を、受け取った量の合計（目標）に、臨界減衰のバネで近づける。Unity の `SmoothDamp` と同じ式を使う。
   - 動き始めは速さ 0 から加速し、目標に近づくと減速する（最初と最後がゆっくり）
   - 目標との差が大きいほど速く動く（量が多いと速い）
   - 最後には、受け取った量をちょうど全部送る（量は変わらない）
2. **速さの上限を広げる**：`speedPercent` の上限を 100 から 300 にする。ユーザーは 100 で「ほんのちょっと少ない」と言っている。

今の均等割り（`linear`）は残し、設定で選べるようにする。

## 1. 変更してよいファイル

- `app/LogiGap.Core/Settings.cs`
- `app/LogiGap.Core/Protocol.cs`（または新規 `app/LogiGap.Core/WheelSpring.cs`）
- `app/LogiGap.Core/GapSession.cs`
- `app/LogiGap.Tests/` の下（既存テストの修正と、新しいテストの追加）
- `app/README.md`

ほかのファイルは変えない。特に `app/publish/` と `logs/` には触れない（ユーザーの設定は、後で Claude が書き換える）。

## 2. 設定（Settings.cs）

`ThumbSettings` に 2 つ足す。

```csharp
// 肩代わり中の滑らか化の方式。"linear" = 1 段を smoothMs の間に均等に分ける（指示書E）。"spring" = バネの動き（指示書I）。
public string SmoothMode { get; init; } = "linear";
// spring のバネの時間（ms）。大きいほど、ゆっくり動き出して、ゆっくり止まる。
public int SpringMs { get; init; } = 50;
```

`Validate` に足す・変える。

- `SmoothMode` が `"linear"` / `"spring"` 以外なら `InvalidDataException("thumbWheel.smoothMode は linear / spring のどちらかです")`
- `SpringMs` が 10〜500 でなければ `InvalidDataException("thumbWheel.springMs は 10〜500 です")`
- `SpeedPercent` の範囲を 0〜100 から **0〜300** にする（文言は今のままでよい）
- `SmoothMs == 0` のときは、`SmoothMode` に関係なく、今までどおり分けずにすぐ送る

`SmoothIntervalMs`（既定 8）は、spring でも送る間隔として使う。

**理由**：既定を `linear` のままにすれば、既存のテストと今の動きが変わらない。実際の設定は Claude が `config.json` で `spring` にする。

## 3. バネの滑らか化（`WheelSpringSmoother`）

`WheelSmoother` と同じ形の、時刻を引数で受け取る純粋なクラスにする。スレッドもタイマーも持たない。

```csharp
public sealed class WheelSpringSmoother
{
    public void Configure(int springMs, int intervalMs);  // 変更時は Reset も行う
    public void Add(int amount, TimeSpan now);
    public int Take(TimeSpan now);                         // 送る量。送るものが無ければ 0
    public TimeSpan? NextDue { get; }                      // 次に Take すべき時刻。動いていなければ null
    public void Reset();                                   // 残りを捨てる
}
```

### 3.1 状態

- `target`（double）：受け取った量の合計
- `position`（double）：バネの位置
- `velocity`（double、量/秒）
- `emitted`（long）：送った量の合計（整数）
- `last`（TimeSpan）：前回 `Take` した時刻
- `lastAdd`（TimeSpan）：前回 `Add` した時刻
- 動いているかどうか、`nextDue`

### 3.2 Add(amount, now)

- `amount == 0` なら何もしない
- 動いていなければ：`target = position = velocity = 0`、`emitted = 0`、`last = now`、`nextDue = now`（1 回目の `Take` はすぐ）
- 動いていて、`amount` の向きが「まだ送っていない残り（`target - emitted`）」の向きと逆なら：残りを捨てる。`target = position = emitted`、`velocity = 0`、`nextDue = now`
- `target += amount`、`lastAdd = now`

**理由**：逆向きに回したとき、前の向きの残りが流れ続けないようにする（指示書E と同じ考え方）。

### 3.3 Take(now)

- 動いていない、または `now < nextDue` なら 0
- `dt = (now - last)` 秒。ただし 0.001〜0.05 秒に収める（遅れても、大きく飛ばない）。`last = now`
- バネの式（臨界減衰、`SmoothDamp` と同じ）。`T = springMs / 1000`：

  ```
  omega = 2 / T
  x = omega * dt
  e = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x)
  change = position - target
  temp = (velocity + omega * change) * dt
  velocity = (velocity - omega * temp) * e
  position = target + (change + temp) * e
  ```

- 目標を通り過ぎたら（`position` が `target` の向こう側に出たら）、`position = target`、`velocity = 0`
- 終わりの判定：`|target - position| < 0.5` かつ `|velocity| < 20`、または `now - lastAdd >= 10 × T` → `position = target`、動きを止める（`NextDue` は null）
- 送る量：`whole = (long)Math.Truncate(position)`、`out = whole - emitted`、`emitted = whole`。終わったときは `whole = (long)target`（target は整数の合計なので、端数は残らない）
- 動きが続くなら `nextDue += interval`。それでも `now` 以下なら `nextDue = now + interval`（遅れたときに、まとめて送らない）
- 1 回の `Take` で返すのは 1 回分だけ。`out` が 0 の回もある（GapSession は 0 なら送らない）

### 3.4 守ること

- 終わったときの `emitted` の合計は、受け取った量の合計と**ちょうど**同じ（逆向きで捨てた分を除く）
- 1 回の `Take` が返す量の向きは、`target - (前回までの emitted)` の向きと同じ（行き過ぎて戻る動きはしない）

## 4. GapSession の変更

- `SmoothMode` に合わせて、`WheelSmoother`（linear）か `WheelSpringSmoother`（spring）を使う。共通の小さなインターフェース（例：`IWheelSmoother`）にまとめてよい
- コンストラクタと `UpdateSettings` で、今の設定の方を `Configure` する。`UpdateSettings` では、方式が変わっても変わらなくても残りを捨てる（今と同じ）
- `Pump`、`WorkerWaitMilliseconds`、`SetPaused` は、今の `smoother` を使っている箇所をそのまま新しい形に置き換える。動き（1 回の `Pump` で 1 回分、一時停止で捨てる、橋渡しの終了後も残りは送る）は変えない

## 5. テスト（xUnit、既存の `Harness` を使う）

### 5.1 既存テストの扱い

既定は `linear` なので、既存テストは変えずに合格するはず。`speedPercent` の範囲のテストがあれば、上限を 300 にして直す。

### 5.2 新しいテスト（時刻は 8ms ずつ進める）

1. spring（50ms / 8ms）で `Add(1200, 0)`：終わるまで `Take` を続けると、合計がちょうど 1200。最後は `NextDue` が null。10 × T（500ms）以内に終わる
2. 動き始めと終わりがゆっくり：1 の各回の量で、最初の 1 回と最後の 1 回は、最大の 1 回より小さい
3. 量が多いと速い：`Add(2400)` の最大の 1 回は、`Add(1200)` の最大の 1 回より大きい。終わるまでの回数の差は 3 回以内
4. 負の量も同じ（`Add(-1200)` で合計 -1200）
5. 動いている途中に同じ向きを追加：合計は 2 つの量の和
6. 逆向き：`Add(1200, 0)`、何回か `Take` した後に `Add(-600, t)` → その後に返す量はすべて 0 以下。最後の `emitted` は「逆向きの前に送った分 - 600」
7. 遅れ：`Add(1200, 0)`、`Take(0)`、次に `Take(500ms)` → 1 回分だけ返し、合計は 1200 を超えない。`NextDue` は 508ms
8. `Reset` と `Configure` で残りが消える
9. 設定：`smoothMode` の既定は `linear`、`springMs` の既定は 50。JSON で読み書きできる。`"Spring"`・`""` は例外。`springMs` 9・501 は例外。`speedPercent` 300 は通り、301 は例外
10. GapSession（hook、spring 50ms、`speedPercent` 100、`DirectionSign = 1`）：Active で `TryTakeHorizontal(120)` → 8ms ずつ時刻を進めて `Pump` を繰り返すと、`WheelEvents` の合計が -120。0 は送られていない。`WorkerWaitMilliseconds` は動いている間 8 以下
11. GapSession：spring でも `SetPaused(true)` で残りは送られない。Options+ がサムホイールを設定して橋渡しが終わっても、残りは送られる

## 6. README

指示書H の節の後に、指示書I の節を足す。

- 0.1 の表（Google スプレッドシートでの比較）
- `smoothMode`（linear / spring）と `springMs` の意味。spring では動き始めと終わりがゆっくりになり、量が多いほど速く動くこと。量は変わらないこと
- `speedPercent` の上限を 300 にしたこと
- 実機で合わせるときの目安：ゆっくり動き出しすぎるなら `springMs` を小さく、止まり方が急なら大きくする

## 7. 守ること

- `app/` の外は変えない（`logs/` は読むだけ。`app/publish/` には触れない）
- アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しない
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで直す
- パッケージを追加しない。環境変数を変えない
- 判断が必要な点は、この指示書の「理由」に沿って決め、その箇所に `// 要確認:` を残す

## 8. 完了報告

日本語で、次の形で書く。

1. 変更したファイルと、それぞれの変更内容
2. ビルドとテストの結果（テストの件数と合否）
3. 指示書から外れた点と、その理由（無ければ「無し」）
4. 新しく残した `// 要確認:` の一覧
5. ユーザーが実機で最初に確かめること
