`codex/指示書Y_画面の端でも効くようにする.md` の続き（3 回目）です。前回足した診断の記録（`diagnostics.scrollProbe`、今オン）を、ユーザーに試してもらって Claude が読みました（`app/publish/logs/app_20261121.log` の `ScrollProbe` の行。18:32〜18:34）。

分かったこと：
1. **Acrobat**：スクロールバー（クラス `ScrollBar`）は `Classic@cursor` で正しく見分けられている。それなのに高速スクロールで 1 ミリも動かない（ふつうのホイールなら動く）。高速スクロールは、元の delta に倍率（50）を掛けた **1 回の大きなホイール**を SendInput で送っているので、Acrobat はそれを受け付けていないと考えられる
2. **VS Code**（Chromium）：画面の右端（例 `cursor=(2561,-1223) edgeLRTB=2559,0,…`）でも `Chromium@cursor` で見分けられている。それでも速くならない。VS Code では画面のいちばん端の列が編集欄の外（ページの中で何もない所）にあって、そこで回したホイールはどこにも効かない、と考えられる（スクロールバーの上では効いている）
3. （ついで）サブモニターの上の端で `edge=NoMonitor` が出ている（カーソルの y がモニターの範囲の外の値）。害はないが、端の補助が効かない原因になりうるので、モニターの範囲の外の点は一番近いモニターに寄せてから扱う

直すこと：
- **高速スクロールの送り方**：大きな 1 回ではなく、**元の delta の大きさのホイールを倍率の回数**だけ送る（SendInput に並べてまとめて送る。自分の入力の印は今までどおり）。数が多すぎるとき（倍率 × 回の合計が多い）は、1 回の送り出しを 100 個までに区切る
- **画面の端の Chromium**：端の補助で「内側の点」を使って Chromium と見分けたとき、または、カーソルの点で `Chromium@cursor` でもモニターの端から 2 ドット以内のときは、ホイールを SendInput ではなく、**その `Chrome_RenderWidgetHostHWND` に `PostMessage(WM_MOUSEWHEEL)` で、lParam を内側の点（画面の座標）にして**送る（高速スクロールなら倍率の回数）。Chromium は lParam の座標の下の要素にホイールを届けるので、端の列ではなく、内側の編集欄やスクロールバーに効くはず。今の「Chromium の窓には届け直さない」決まりは、この場合だけ外す（`wheel.edgeRedirect` の設定とは別。端の高速スクロールの一部として動く）
- 診断の記録に、送り方（SendInput／PostMessage）と送った回数を足す
- テストを足す（分けて送る回数・区切り、Chromium の端で PostMessage を選ぶ条件、範囲の外の点を寄せる）。今あるテストは全部通る
- 決まり（§0）は前回と同じ。ビルドとテストは最初から `dotnet build|test app/PartsTray.sln --no-restore --disable-build-servers "-p:TargetPlatformSdkPath=C:/Program Files (x86)/Windows Kits/10" -p:TargetPlatformDisplayName=Windows`
- 最後のメッセージ（日本語）：変えたこと、テスト件数、Claude が実機で確かめること
