# 指示書F：Options+ の設定の検出で、swid を 14 に固定しない

## 0. この指示書の目的

### 0.1 実機でわかったこと（11/19 19:21 の試験と、記録ツールのログより）

LogiGap は、マウスの応答のうち **swid が 14（0xE）** のものだけを Options+ の設定として見ている（`GapSession.Receive` の `report.Swid == 14`）。ところが Options+ は、毎回 14 を使うわけではない。

| 時刻（記録ツールのログ） | Options+ と同じ設定の手順を書いた swid |
|---|---|
| 14:25 | 0xA |
| 14:27 | 0xC |
| 16:17 | 0xB |
| 16:24〜17:10（Options+ のエージェントを再起動した後） | 0xE |
| 19:18〜（今） | 0xB |

19:21 の試験の流れ：

1. 19:21:23.040 リンク確立。LogiGap が肩代わりを開始（Active）
2. 19:21:35.392 swid=0xB が setSensorDpi。続けて setCidReporting（0x52, 0x53, 0x56, 0xC3, 0xC4, 0x1A0, 0x50, 0x51）、setWheelMode
3. 19:21:36.727 swid=0xB が THUMB_WHEEL の setThumbwheelReporting
4. LogiGap は swid=0xB を Options+ と見なさないので、Active のまま。引き継ぎの通知も出ず、橋渡しも止まらない

この手順は、17:10 に swid=0xE で記録した Options+ の手順と同じ（同じ CID、同じ flags=0x23、同じ順番）。swid が違うだけで、書いているのは Options+ である。

### 0.2 方針

swid が **0 でも 5（LogiGap 自身）でもない応答**（1〜15 のうち 5 以外）を、すべて「Options+（ほかのソフト）の設定」として扱う。見るのは今と同じく、setCidReporting・setThumbwheelReporting・setSensorDpi の応答だけ。

**理由**：

- Options+ の swid は接続やエージェントの起動ごとに変わる。どれになるかは予測できない
- Options+ 以外のソフトがこれらの設定を書いた場合も、LogiGap が書き続けると設定を奪い合うことになる。どのソフトであっても、引くのが安全
- swid 0 は通知（マウスから自発的に来るもの）なので含めない

## 1. 変更してよいファイル

- `app/LogiGap.Core/GapSession.cs`
- `app/LogiGap.Core/Settings.cs`
- `app/LogiGap.Tests/` の下（既存テストの修正と、新しいテストの追加）
- `app/README.md`

ほかのファイルは変えない。特に `app/publish/` と `logs/` には触れない（ユーザーの設定は、後で Claude が書き換える）。

## 2. GapSession の変更

### 2.1 検出の条件

`Receive` の `report.Kind == ReportKind.Response && report.Swid == 14` を、次の条件に変える。

```
report.Kind == ReportKind.Response && report.Swid != 0 && report.Swid != 5
```

5 は、既存のコードと同じく定数のままでよい（`Match` の `r.Swid != 5` と合わせる）。`earlyOptions` に貯める処理と `ObserveOptions` を呼ぶ処理は、今のまま同じ条件の中に置く。

### 2.2 ログに swid を残す

`ObserveOptions` で Retreated にするときの理由に、swid を 16 進で入れる。

- 今：`Options+ 設定開始: setSensorDpi`
- 後：`Options+ 設定開始（swid=B）: setSensorDpi`

引き継ぎの通知の文（`Options+ に引き継ぎました（肩代わり …秒）`）は変えない。

`HidReport` に swid が無い経路（早期の再確認など）で困る場合は、`ObserveOptions` に swid を引数で渡す形でよい。

## 3. 設定（Settings.cs）

- `GapSettings.OptionsPlusSwid` を削除する。古い `config.json` に `"optionsPlusSwid": 14` が残っていても、読み込みで無視される（既存の JSON の読み方のまま。例外にしない）
- `Validate` の条件から `g.OptionsPlusSwid != 14` を外し、文言を `安全のため WPID=B042 / swid=5 は固定です` にする

## 4. テスト（xUnit、既存の `Harness` を使う）

### 4.1 既存テストの扱い

- `Harness.Options(...)` に `byte swid = 14` の引数を足す（既定は 14 のままにして、既存テストの期待値は変えない）
- `OptionsPlusSwid` を参照しているテストがあれば、その部分だけ直す。期待値は変えない

### 4.2 新しいテスト

1. Active のときに、swid が 0x1・0xA・0xB・0xF の setSensorDpi の応答 → Retreated になり、通知 `Options+ に引き継ぎました` が出る。状態変化のログに `swid=B`（0xB のとき）が含まれる
2. 同じく swid 0xB の setCidReporting・setThumbwheelReporting の応答でも Retreated になる。setThumbwheelReporting なら橋渡しも終わる（ログ `橋渡しを終了（Options+ がサムホイールを設定）`）
3. swid 5 の同じ応答（LogiGap 自身の要求と一致しないもの）では Retreated にならない
4. swid 0 の通知（例：THUMB_WHEEL の回転、REPROG_CONTROLS_V4 のボタン）では Retreated にならない
5. swid 0xB の、対象外の機能の応答（例：getThumbwheelStatus＝THUMB_WHEEL fn1、ROOT の ping）では Retreated にならない
6. 接続直後、機能の番号がまだわからないうち（`discoveryStep` の途中）に swid 0xB の setSensorDpi の応答が来た場合も、今の swid 14 と同じく `earlyOptions` で後から判定されて Retreated になる
7. 設定：`{"gapFiller":{"optionsPlusSwid":14}}` も `{"gapFiller":{"optionsPlusSwid":11}}` も読めて、`Validate` が通る。保存した JSON に `optionsPlusSwid` が出ない

## 5. README

- 「Options+ の検出」の説明を、swid 14 固定から「swid が 0・5 以外の応答で、setCidReporting・setThumbwheelReporting・setSensorDpi のどれかを書いたら引く」に直す。0.1 の表を短く載せる（Options+ の swid は 0xA・0xB・0xC・0xE を確認済み）
- `optionsPlusSwid` は廃止。古い設定に残っていても無視されること
- 151 行目付近の「WPID と swid は変更を拒否します」は、「WPID と LogiGap 自身の swid（5）は変更を拒否します」にする

## 6. 守ること

- `app/` の外は変えない（`logs/` は読むだけ。`app/publish/` には触れない）
- アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しない
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで直す
- パッケージを追加しない。環境変数を変えない
- 判断が必要な点は、この指示書の「理由」に沿って決め、その箇所に `// 要確認:` を残す

## 7. 完了報告

日本語で、次の形で書く。

1. 変更したファイルと、それぞれの変更内容
2. ビルドとテストの結果（テストの件数と合否）
3. 指示書から外れた点と、その理由（無ければ「無し」）
4. 新しく残した `// 要確認:` の一覧
5. ユーザーが実機で最初に確かめること
