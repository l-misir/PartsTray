高速スクロールを、元の delta のまま倍率の回数だけ送る方式に変更しました。倍率 50 なら同じホイール入力を 50 個まとめて `SendInput` に渡します。1 回の送信は最大 100 個で、各入力に PartsTray 自身の印を付けます。

Chromium では、端の補助がオンで、モニター端から 2 ピクセル以内の元の点か内側の点でスクロールバーを検出したとき、検出した `Chrome_RenderWidgetHostHWND` に内側の画面座標で `WM_MOUSEWHEEL` を投函します。`wheel.edgeRedirect` がオフでも働きます。モニター範囲外の点は、最寄りのモニター内に寄せてから判定します。

指定コマンドで**ビルド成功（警告 0・エラー 0）、テストは追加 26 件を含む全 1,162 件が合格**しました。失敗・スキップは 0 件です。

### Claude が実機で確かめること

修正版で高速スクロール・端の補助・診断をオン、倍率を 50 にします。`ScrollProbe wheel` の `sent` は Windows が受け付けた件数です。同じ `cursor` の判定行と、実際の画面の動きを照合してください。[README の確認手順](<../../app/README.md:80>)

| 場所 | 確認する動きとログ |
|---|---|
| Acrobat | 長い PDF の縦バーで上下に回し、`Classic@cursor`、`transport=SendInput requested=50 sent=50` が出たときに、ページが両方向へ高速で動く。 |
| VS Code | `wheel.edgeRedirect` をオフにして、最大化したエディターの右端で回す。`Chromium@cursor` または `Chromium@inside` で `transport=PostMessage requested=50 sent=50` が出て、`target` が判定行の `inside` と一致し、本文が高速で動く。 |
| エクスプローラー | 最大化した長い一覧の同じ高さで比べ、本文中央は通常速度、縦バーと画面右端は判定後に高速になる。 |
| サブモニター上端 | 以前 `NoMonitor` が出た位置で、範囲外の入力に `clamped` が付き、`edgeLRTB` の 3 番目（上端までの距離）が `0` になる。 |