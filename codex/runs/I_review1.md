# 指示書I レビュー 1 回目（Claude）

## 経過

Codex は 20:26:36 に始めて、本体（`Settings.cs`、`Protocol.cs` の `IWheelSmoother`、新規 `WheelSpring.cs`、`GapSession.cs` の切り替え）を書いた。20:28 に使用量の上限（再開は 21:40）で止まり、完了報告（`I_report.md`）は空のまま。テストと README は書かれていなかった。

ユーザーの了承（小さな修正は Claude でよい）に沿って、Claude がテスト 19 件と README を書いた。

## 結論

**合格。**

- ビルド：警告 0・エラー 0（`--no-restore`）
- テスト：**314 件すべて合格**（I で 19 件追加）
- 作業フォルダの外：変更なし（増えたのは `codex/runs/I_report.md`、空）

## 確認したこと

| 項目 | 結果 |
|---|---|
| 設定 | `smoothMode`（linear / spring、既定 linear）、`springMs`（10〜500、既定 50）、`speedPercent` の上限 300。文言は指示書どおり |
| バネ | `SmoothDamp` の式どおり。dt は 0.001〜0.05 秒に収める。行き過ぎたら目標で止める。終わりは「差 0.5 未満かつ速さ 20 未満」または「最後の追加から 10 × T」。終わったときは target の整数をそのまま送るので、端数が残らない |
| 逆向き | まだ送っていない残りと逆向きなら、`target = position = emitted`、速さ 0 にしてから足す |
| GapSession | `CreateSmoother` で方式を選ぶ。`UpdateSettings` で作り直す（残りを捨てる）。`Pump`・`WorkerWaitMilliseconds`・`SetPaused` は変わっていない |
| `// 要確認:` | 指示書の §5.2(7)（500ms 後の Take）が 10 × T の終了と重なる点。終了条件を優先したのは正しい。テストは 300ms に直した |

## 動きの目安（Python で同じ式を計算）

| springMs | 本来の 1 段（120）の送り方 | 1 回転（2400）にかかる回数・1 回の最大 |
|---|---|---|
| 30 | 0, 14, 23, 22, 18, 13, 10, … 約 130ms | 23 回・460 |
| 50 | 0, 6, 11, 14, 14, 13, 11, 10, … 約 200ms | 36 回・278 |
| 80 | 0, 2, 6, 7, 8, 9, 9, 8, … 約 310ms | 56 回・175 |

## 実機で確かめること

1. `config.json` を `smoothMode: spring`、`springMs: 50`、`speedPercent: 110` にする
2. レシーバーを抜き差しし、肩代わり中と Options+ で、同じアプリで回し比べる
3. 動き始め・止まり方・量が Options+ に近いこと。引き継ぎで止まらないこと
