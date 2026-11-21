`codex/指示書W_100の数字の大きさ.md` の続き（4 回目）です。細字は `uniform`（字の黒い部分で詰め、字の間 1 ドット、Tahoma Bold）になり、実機に入りました。

ユーザー（11/21）：「いい感じですが、これフォント変えたほうが良くないですか？ 特に 5 とかなんか違うような…」

- 細字の数字の**フォントを選べる**ようにしてください：設定 `monitor.digitFont`（フォントの名前。既定は今の `Tahoma`、太さは今と同じく太字）。画面には出さない内部の項目でよい（Rust 版の設定画面で出す）。そのフォントがその PC にないときは Tahoma に戻す
- **見本**：`codex/見本/数字100/フォント比べ.png` を書き出してください。候補は Windows 10・11 に最初から入っているもの：Tahoma Bold（今）・Segoe UI Bold・Segoe UI Semibold・Verdana Bold・Arial Bold・Bahnschrift（SemiBold と Bold）・Consolas Bold・Yu Gothic UI Bold・Meiryo UI Bold。行がフォント、列が「0123456789」と「8・37・55・99・100・150」。`uniform` の詰め方で、20 ドットと 16 ドット、暗い背景、実寸と 8 倍（8 倍は 20 ドットだけでよい）。特に **5** の形が分かるように
- 小さい大きさで字がつぶれるもの・はみ出すものは、見本の行の横に印を付ける（例「16 ドットで 5 と 6 の区別がつきにくい」）。判断の根拠も報告に書く
- 既定は Tahoma のまま。ユーザーが見本から選んだら Claude が既定を変える
- テスト：`digitFont` の読み込み・ないフォントのとき Tahoma に戻る・どのフォントでも範囲（0〜150）の数が枠に収まる
- 決まり（§0）は前回と同じ。ビルドとテストは最初から `dotnet build|test app/PartsTray.sln --no-restore --disable-build-servers "-p:TargetPlatformSdkPath=C:/Program Files (x86)/Windows Kits/10" -p:TargetPlatformDisplayName=Windows`
- 最後のメッセージは、指示書 §3 の形の完了報告（日本語）
