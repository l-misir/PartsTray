# 指示書E レビュー 1 回目（Claude）

## 結論

**合格。** 修正の依頼はない。

- ビルド：警告 0・エラー 0（`--no-restore`）
- テスト：**249 件すべて合格**（E で 40 件追加）
- 作業フォルダの外：変更なし。チェックサムで確かめた（増えたのは `codex/runs/E_prompt.md` と `E_report.md` だけ）

## 確認したこと

| 項目 | 結果 |
|---|---|
| 設定 | `smoothMs`（既定 40）・`smoothIntervalMs`（既定 8）・`diagnostics.wheelTrace`（既定 false）。範囲外は指示書の文言で例外 |
| `WheelSmoother` | 1 回目はすぐ、以後は間隔ごとに 1 回分。切り上げで早めに送り切る。同じ向きは残りと合わせて分け直し、逆向きは前の残りを捨てる。遅れてもまとめて送らない |
| GapSession | 滑らか化は hook の橋渡しだけ（divert は変えていない）。`WorkerWaitMilliseconds` に次の送信時刻が入る。一時停止で残りを捨て、橋渡しの終了後は残りを送る。終了のログに受け取り・送信の件数と合計 |
| 高精度タイマー | `CreateWaitableTimerExW(..., HIGH_RESOLUTION)` を `WaitHandle` で包み、30ms 以下の待ちだけに使う。作れないときはログ 1 行で従来の待ちに戻る |
| 記録（フック） | `0x20E` だけ。Extra の判定の外で記録するので、LogiGap 自身の送信も残る。フックのスレッドではキューに入れるだけ。例外は抑止の判定に影響しない |
| 記録（書き込み） | 背景スレッドが約 250ms ごとに `logs/wheel_yyyyMMdd.log` に追記。キューの上限 20,000、起動ごとの上限 200,000 件 |

## 小さな点（直さなくてよい）

- 高精度タイマーは、`wake` で先に起きたときも次の `Arm` で上書きされる。合図が残っていても、早く 1 回起きるだけで害はない
- 記録の切り替え（`SetEnabled`）は UI スレッドで背景スレッドの終了を待つが、停止の合図ですぐ抜けるので問題ない

## 実機で確かめること

1. 発行後、`config.json` に `"smoothMs": 40, "smoothIntervalMs": 8` と `"diagnostics": { "wheelTrace": true }` を足す
2. レシーバーを抜き差しし、肩代わり中にサムホイールを回す。Options+ に近い滑らかさで、動き出しが遅れないこと
3. 引き継ぎの後も回し、Options+ の出力を記録に残す
4. Claude が `logs/wheel_*.log` と記録ツールのログを照らし合わせ、`speedPercent` と滑らか化の値を決める。終わったら `wheelTrace` を false に戻す
