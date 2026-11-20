# 指示書H：接続判定で、再起動した Options+ がまだ設定していないことを見分ける

## 0. この指示書の目的

### 0.1 実機でわかったこと（11/19 19:49、記録ツールのログと LogiGap のログより）

| 時刻 | 出来事 |
|---|---|
| 19:49:09.97 | Options+（swid=A）の設定を見て、LogiGap が引き継いだ |
| 19:49:11 | Options+ のエージェント（`logioptionsplus_agent`）を止めた。マウスのボタンとサムホイールは、横取りのまま残った |
| 19:49:15.1 | エージェントを起動した |
| 19:49:15.84 | マウスのリンクがつなぎ直された（Options+ の起動による。19:34:18 も同じ） |
| 19:49:15.99 | 接続判定：`divert=1 Options+=True previousActive=False` → **「Options+ 設定済み」で Retreated** |
| 19:49:15〜30 | サムホイールは止めた Options+ の横取りのまま。回転通知 約 950 件を誰も処理せず、横スクロールが出なかった。ボタンも同じく、誰も処理しない |
| 19:49:30.04 | 新しい Options+ が設定を開始。getThumbwheelStatus は横取り=1（残っていた） |

今の接続判定は「中ボタン（0x52）が横取りで、Options+ が動いていれば、設定済み」と見なす。しかし、この横取りは**止めた前の Options+ が残したもの**で、新しい Options+ はまだ何も設定していなかった。Options+ が異常終了した後の再起動、更新による再起動でも同じことが起きる。

### 0.2 方針

Options+ のエージェントの**起動時刻**を使う。接続判定で `divert=1`・Options+ が動いている・previousActive でない、の 3 つがそろったとき、次のどちらかなら今までどおり「設定済み」で引く。

- (a) エージェントが、この GapSession を作る前から動いている（LogiGap はそれより前の様子を知らないので、設定済みと見なす。今までどおり）
- (b) エージェントの起動より後に、Options+ の設定（setCidReporting・setThumbwheelReporting・setSensorDpi のどれか、swid は 0・5 以外）を一度でも見た

どちらでもない（エージェントが GapSession より後に起動し、その後 Options+ の設定を一度も見ていない）なら、横取りは前の Options+ の残りと見なし、**Active にして肩代わりする**。肩代わりの適用の最後に、指示書G の処理で、サムホイールに残った横取りも解除される。

**理由**：

- 新しい Options+ は、起動から設定まで約 15 秒かかる（19:49:15 → 19:49:30、19:34:18 → 19:34:33）。その間を LogiGap が埋める
- 起動時刻はプロセスの作成時刻なので、Options+ の設定の手順や swid に左右されない
- 起動時刻がわからないとき（取得に失敗したとき）は、今までどおり引く。わからないときに肩代わりして、Options+ と取り合うよりは安全

## 1. 変更してよいファイル

- `app/LogiGap.Core/GapSession.cs`
- `app/LogiGap/GapModule.cs`
- `app/LogiGap.Tests/` の下（既存テストの修正と、新しいテストの追加）
- `app/README.md`

ほかのファイルは変えない。特に `app/publish/` と `logs/` には触れない。

## 2. GapSession の変更

### 2.1 コンストラクタ

最後に 2 つの省略可能な引数を足す（既存の呼び出しとテストはそのまま動くこと）。

```csharp
Func<DateTime?>? optionsStartedUtc = null,   // Options+ のエージェントの起動時刻（UTC）。動いていない・わからないときは null
Func<DateTime>? utcNow = null                // 現在時刻（UTC）。既定は () => DateTime.UtcNow
```

- コンストラクタで `createdUtc = utcNow()` を記録する
- `lastOptionsConfigUtc`（`DateTime?`、初期値 null）を持つ

### 2.2 Options+ の設定を見たときの記録

`ObserveOptions` で、setCidReporting・setThumbwheelReporting・setSensorDpi のどれかと判定したとき（今の `if (!cidWrite && !thumbWrite && !dpiWrite) return;` を通過した後）に、`lastOptionsConfigUtc = utcNow()` にする。状態（Active・Retreated など）に関係なく記録する。

### 2.3 接続判定

`NextCommand` の「接続判定 getCidReporting」の成功処理を次のようにする。

```
bool running = optionsRunning();
bool fresh = false;
DateTime? started = null;
if (divert && running && !inheritedActive && optionsStartedUtc != null)
{
    started = optionsStartedUtc();
    fresh = started is { } s && s > createdUtc && (lastOptionsConfigUtc is not { } seen || seen < s);
}
```

- ログの行に、起動時刻と判定を足す：
  `接続判定: divert=1 Options+=True previousActive=False Options+起動=19:49:15.123 起動後の設定=なし`
  - 起動時刻は現地時刻の `HH:mm:ss.fff`。わからなければ `不明`。`optionsStartedUtc` が null のとき、または判定に使わなかったときは、この 2 項目を書かない
  - 起動後の設定は `あり` / `なし`
- `divert && running && !inheritedActive && !fresh` → 今までどおり Retreated（`接続判定: Options+ 設定済み`）
- `fresh` → Active。理由は `接続判定: Options+ は起動後まだ設定していない（前の横取りが残っている）`。肩代わり開始の通知は、今の Active と同じく出す
- それ以外 → 今までどおり Active（`接続判定: 設定の肩代わりが必要`）

`optionsStartedUtc()` が例外を投げたら、`started = null`・`fresh = false`（今までどおり引く）にして、例外は外に出さない。

## 3. GapModule の変更

`GapSession` を作るときに、`optionsStartedUtc` を渡す。

- `Process.GetProcessesByName("logioptionsplus_agent")` の各プロセスの `StartTime.ToUniversalTime()` を読み、**いちばん新しい**時刻を返す。1 つも無ければ null
- 1 つのプロセスで読めなかったら（例外）、そのプロセスは飛ばす。全部読めなければ null。読めなかったことは、アプリのログに 1 回だけ `Options+ の起動時刻を読めません: {ex.Message}` と書く
- 取ったプロセスは、今の `OptionsRunning()` と同じく必ず `Dispose` する

**理由**：いちばん新しい時刻を使うのは、古いエージェントと新しいエージェントが同時に見えたときに、「新しい方はまだ設定していない」を見落とさないため。

## 4. テスト（xUnit、既存の `Harness` を使う）

`Harness` に次を足す。既存のテストの期待値は変えない。

- `public DateTime UtcNow { get; set; }`（初期値は固定の日時。例：2026-11-19 10:00:00 UTC）
- `public DateTime? OptionsStartedUtc { get; set; }`（初期値 null）
- コンストラクタで `optionsStartedUtc: () => OptionsStartedUtc, utcNow: () => UtcNow` を渡す

`OptionsStartedUtc` が null のとき、判定は今までどおりになるので、既存テストはそのまま合格するはず。

新しいテスト（`h.Cids[0x52] = 1` で、接続判定の divert=1 を作れる）：

1. エージェントが GapSession より前に起動（`OptionsStartedUtc = UtcNow - 1 時間`）、divert=1 → Retreated（`Options+ 設定済み`）。ログに `起動後の設定=なし` と起動時刻
2. エージェントが GapSession より後に起動（`UtcNow` を 10 分進め、`OptionsStartedUtc` をその 5 秒前）、Options+ の設定を見ていない、divert=1 → **Active**。理由のログ `Options+ は起動後まだ設定していない（前の横取りが残っている）`、通知 `肩代わりを開始しました`。`h.Thumb = 1` なら、指示書G の解除（fn2 `(0, 0)`）も送られる
3. 2 と同じ起動時刻で、エージェントの起動より後に Options+ の設定（`h.Options()`）を見た後につなぎ直す（`h.Link()`）、divert=1 → Retreated
4. 3 の後、エージェントがさらに再起動した（`OptionsStartedUtc` を、最後に設定を見た時刻より後にする）→ つなぎ直すと Active
5. `OptionsStartedUtc` が null（不明）→ Retreated（今までどおり）
6. `optionsStartedUtc` が例外を投げる → Retreated。例外は外に出ない（`Harness` を使わず、`GapSession` を直接作ってよい）
7. Options+ が動いていない（`OptionsRunning = false`）→ 起動時刻に関係なく Active（今までどおり）。ログに起動時刻の項目が無い
8. previousActive の場合の動きは変わらない（既存テストが合格していればよい）

## 5. README

指示書G の節の後に、指示書H の節を足す。

- 何が起きたか（0.1 の表を短く）
- 接続判定の新しい規則（0.2 の (a)(b) と、どちらでもなければ肩代わりすること）。起動時刻がわからないときは今までどおり引くこと
- ログの見方：`Options+起動=… 起動後の設定=あり/なし`、`Options+ は起動後まだ設定していない（前の横取りが残っている）`
- 「接続ごとの判定」の説明（判断事項と制約の節）にも、この規則を 1 文で足す

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
