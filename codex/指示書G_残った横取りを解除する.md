# 指示書G：肩代わりの開始時に、サムホイールに残った横取りを解除する（hook）

## 0. この指示書の目的

### 0.1 実機でわかったこと（11/19 19:34、記録ツールのログと横スクロールの記録より）

肩代わり中（hook）に、横スクロールが**まったく動かなかった**。

| 時刻 | 出来事 |
|---|---|
| 19:21:36 | Options+（swid=B）がサムホイールを横取り（setThumbwheelReporting）にした |
| 19:30 ごろ | Options+ のエージェントが止まった。マウスのサムホイールは横取りのまま残った |
| 19:32:50 | LogiGap が起動。Options+ がいないので Active（肩代わり）。hook なので THUMB_WHEEL には何も書かない |
| 19:34:18 | Options+ を起動。マウスのリンクがつなぎ直されたが、横取りは解除されなかった（getCidReporting 0x52 も divert=1 のまま） |
| 19:34:19〜33 | 肩代わり中。マウスは HID++ の回転通知（swid=0、約 600 件）だけを送り、本来の横スクロール（WM_MOUSEHWHEEL）は 0 件。フックの記録にも 1 件も無い。**横スクロールが出ない** |
| 19:34:33.489 | Options+（swid=A）の getThumbwheelStatus：横取り=1（残っていた） |
| 19:34:33.6〜36.8 | Options+ の設定の途中で横取りが外れ、本来の横スクロールが出た。LogiGap の橋渡しが送り直した（77 件） |
| 19:34:36.8〜38.3 | Options+ が setThumbwheelReporting を **4 回やり直した**。指示書D で見つけた「設定前に回転通知が届くと、Options+ がやり直して止まる」と同じ形 |

hook は「肩代わりの開始時、サムホイールは本来の横スクロールを出している」という前提で作ってある。横取りが残っていると、この前提が崩れる。

一方、レシーバーを抜き差ししたとき（19:21:23）は、マウス側の設定が消えていた（getCidReporting 0x52 の flags=0x00、19:21:35 の getThumbwheelStatus は横取り=0）。残るのは、抜き差しをしないつなぎ直しや、Options+ が止まった後など。

### 0.2 方針

hook のときは、肩代わりの適用の最後に THUMB_WHEEL の状態を 1 回読む（getThumbwheelStatus、fn1）。

- 横取りが残っていたら（応答の先頭バイトの bit0 = 1）、setThumbwheelReporting(0, 0) で解除して、本来の横スクロール・反転なしに戻す
- 残っていなければ、何も書かない（指示書D のまま）

**理由**：

- 解除すれば、マウスは本来の横スクロールを出すので、橋渡しがそのまま働く
- Options+ に設定前の回転通知が届かなくなるので、Options+ のやり直し（19:34:36〜38 の 4 回）も防げる
- 書くのは「横取りを外す」方向だけ。指示書D で原因だった「LogiGap が横取りにする」書き込みはしない
- 反転は 0 にする。hook は、反転なしの本来の量をソフトで反転・減速する作りだから（19:21:35 と 19:34:36.774 の状態は、どちらも横取り=0・反転ビット=0）

`off` では今までどおり何もしない（読まない・書かない）。`divert` は変えない。

## 1. 変更してよいファイル

- `app/LogiGap.Core/GapSession.cs`
- `app/LogiGap.Tests/` の下（既存テストの修正と、新しいテストの追加）
- `app/README.md`

ほかのファイルは変えない。特に `app/publish/` と `logs/` には触れない。

## 2. GapSession の変更

### 2.1 適用の手順（`NextCommand` の applyStep 6・7）

今の applyStep 6 は「divert 以外なら 8 にして完了」。これを次のようにする。

| モード | applyStep 6 | applyStep 7 |
|---|---|---|
| `off` | すぐ 8 にして `CompleteApply()`（今と同じ） | なし |
| `hook` | **読む**：`Read(features[0x2150], 1, [], "THUMB_WHEEL getStatus 残りの横取りを確認", …)` | **書く**：`Write(features[0x2150], 2, [0, 0], "THUMB_WHEEL 残りの横取りを解除", …)` |
| `divert` | 書く（今と同じ） | 読み戻し（今と同じ） |

hook の読む処理の成功時：

- `p[0] & 1` が 1 → ログ `サムホイール: 横取りが残っていたので解除する（{p[0]:X2} {p[1]:X2}）` を書き、applyStep = 7
- そうでなければ → ログ `サムホイール: 横取りは残っていない（{p[0]:X2} {p[1]:X2}）` を書き、applyStep = 8、`CompleteApply()`
- 応答のパラメーターが 2 バイト未満なら、残っていないものとして扱い、`// 要確認:` を残す

hook の書く処理の成功時：applyStep = 8、`CompleteApply()`。

- `thumbOwned` は立てない（LogiGap は横取りにしていないので、後片付けの対象ではない）
- 書き込みの `Allowed` は既存の `Write` の既定（一時停止でなく、Active）のまま。Options+ が先に設定を始めて Retreated になっていれば、書かない
- 読む処理の応答が、Options+ の設定開始（Retreated・世代の変更）の後に届いた場合も、既存の `Match` の世代チェックで成功処理が呼ばれず、書かないこと

### 2.2 書き込みの許可リスト（`ValidateCommand`）

THUMB_WHEEL fn2 の許可に、次の 1 つを足す。

- `active && p[0] == 0 && p[1] == 0 && sessionSettings.ThumbWheel.Mode == "hook"`

既存の条件（divert の `(1, 1)`、後片付けの `(0, 0)`）は変えない。照会（fn1）は、すでに許可リストにある。

## 3. テスト（xUnit、既存の `Harness` を使う）

`Harness` は THUMB_WHEEL の fn2 で `Thumb = p[0]` にし、fn1 で `[Thumb, Thumb]` を返す。横取りが残っている状態は、`Activate()` の前に `h.Thumb = 1` で作れる。

### 3.1 既存テストの扱い

hook では適用中に fn1 の照会が 1 回増える。次のテストは、**hook のときは「THUMB_WHEEL への書き込み（fn2）が無い」ことを確かめる形に直す**。off のときは今までどおり「THUMB_WHEEL への送信が無い」でよい。期待値のほかの部分は変えない。

- `NativeWheelTests.HookScalesNativeInputWithoutThumbWrites`
- `NativeWheelTests.NonDivertModesNeverOwnOrCleanUpThumb`（後片付けで THUMB_WHEEL の照会・書き込みが無いこと。`h.Thumb = 1` にした後の送信だけを見る形でよい）
- `NativeWheelTests.SettingsResetFractionAndApplySpeedImmediatelyButModeOnNextLink`（つなぎ直した後の送信だけを見る形でよい）
- ほかに、既定（hook）で THUMB_WHEEL の送信が無いことを確かめているテストがあれば同じように直す。`ReplayTests` の最後の検証（キー入力・ホイールとも 0 件）は変えない

### 3.2 新しいテスト

1. hook・横取りが残っている（`h.Thumb = 1`）：`Activate()` の後、fn1 の照会と fn2 `[0, 0]` の書き込みがこの順に 1 回ずつ。`h.Thumb == 0`。ログに `横取りが残っていたので解除する（01 01）` と `適用完了`。`TryTakeHorizontal(120)` は true
2. hook・残っていない（`h.Thumb = 0`）：fn1 の照会が 1 回、fn2 は 0 回。ログに `横取りは残っていない（00 00）` と `適用完了`
3. hook・照会の応答が届く前に Options+ が設定を始めた：`Intercept` で fn1 の照会を受けたときに `h.Options()` を呼ぶ → Retreated。その後に照会の応答が来ても fn2 は送られない。`h.Thumb == 1` のまま
4. hook・照会で横取りが見つかった後、書き込みの前に Options+ が設定を始めた：fn2 は送られない
5. off・`h.Thumb = 1`：THUMB_WHEEL への送信が 1 件も無い。`h.Thumb == 1` のまま
6. hook で解除した後、Options+ に引き継いで `cleanupDelaySeconds` を過ぎても、後片付けで THUMB_WHEEL の照会・書き込みが無い（`thumbOwned` が立っていない）
7. 許可リスト：divert のとき、Active で `(0, 0)` の書き込みは今までどおり拒否される（既存の許可の形が変わっていないこと。直接は呼べないので、divert の適用で `(0, 0)` が送られないことの確認でよい）

## 4. README

指示書F の節の後に、指示書G の節を足す。

- 何が起きたか（0.1 の表を短く）
- hook の適用の最後に THUMB_WHEEL の状態を読み、横取りが残っていたら `(0, 0)` で解除すること。残っていなければ書かないこと
- 書くのは解除だけで、横取りにする書き込みはしないこと
- ログの見方：`横取りが残っていたので解除する（01 01）` / `横取りは残っていない（00 02）`
- 指示書D の「hook では THUMB_WHEEL に書かない」という説明が README にあれば、「横取りが残っているときの解除だけは書く」と直す

## 5. 守ること

- `app/` の外は変えない（`logs/` は読むだけ。`app/publish/` には触れない）
- アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しない
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで直す
- パッケージを追加しない。環境変数を変えない
- 判断が必要な点は、この指示書の「理由」に沿って決め、その箇所に `// 要確認:` を残す

## 6. 完了報告

日本語で、次の形で書く。

1. 変更したファイルと、それぞれの変更内容
2. ビルドとテストの結果（テストの件数と合否）
3. 指示書から外れた点と、その理由（無ければ「無し」）
4. 新しく残した `// 要確認:` の一覧
5. ユーザーが実機で最初に確かめること
