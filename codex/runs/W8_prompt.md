`codex/指示書W_100の数字の大きさ.md` の続き（8 回目）です。今の実機の設定：`monitor.cpuDigitFont = "Bahnschrift SemiBold"`・`temperatureDigitFont = "Terminus"`・`memoryDigitFont = "TerminusBold"`。

ユーザー（11/21）：
- 「Regular（Terminus）なんか見たことあるなって思ったら、CoreTemp のやつにすごい似てます。ただ 0 が 8 に見えるので微妙ですね…」（Terminus の 0 は中に点がある）
- 「おすすめフォント（Bahnschrift）はちょっと論外ですね…」
- 「CoreTemp のフォントを使って、ちょっとだけ大きくすればいいのでは？」→ Core Temp の通知領域のフォントは **Tahoma・標準（太字ではない）・8**（ユーザーが Core Temp の設定で確かめた）。Core Temp は昔ながらの GDI で、にじみ（アンチエイリアス）なしで描いていると考えられる

やること：
1. **フォントの指定に太さと描き方を足す**：`digitFont`・`cpuDigitFont`・`temperatureDigitFont`・`memoryDigitFont` の値を「名前, 太さ, 描き方」の形でも書けるようにする（例 `"Tahoma, Regular, Pixel"`）。太さは `Regular`・`Bold`（省略は今の決まりどおり太字）、描き方は `Smooth`（今のなめらか）・`Pixel`（にじみなし。GDI+ なら `SingleBitPerPixelGridFit`、または GDI の `TextOut` で `NONANTIALIASED_QUALITY`。Core Temp に近いほう）。今の値（`"Tahoma"`・`"Terminus"` など）はそのまま同じ意味で読める
2. **Core Temp と同じ描き方を再現する**：`"Tahoma, Regular, Pixel"` で、大きさは今の `uniform`（0〜150 が字の間 1 ドットで収まる、いちばん大きい大きさ）。Core Temp の 8 ポイントより少し大きくなるはず
3. **Terminus の 0 の中の点を消す**：設定 `monitor.plainZero`（既定 true）。点の絵にしたあと、「0」の外側の線につながっていない内側の点を消す。Terminus 以外でも、0 の中に点や斜線があるフォントに効く
4. **見本** `codex/見本/数字100/CoreTemp風の比べ.png`：行 ①Core Temp と同じ（Tahoma 標準 8 ポイント・にじみなし。Core Temp の大きさのまま）②`"Tahoma, Regular, Pixel"`（uniform の大きさ）③`"Terminus"`（0 の点を消したもの）④今の既定（Tahoma 太字・なめらか）。列「0・8・37・55・99・100・150」、20 と 16 ドット、暗い背景と明るい背景、実寸と 8 倍
5. テスト：指定の読み方（古い書き方も）、0 の点を消す、大きさの選び方
- 既定はまだ変えない（ユーザーが見本で選ぶ）
- 決まり（§0）は前回と同じ（`rust/` に触れない、アプリを起動しない、ダウンロードしない、`git` で書き換えない、csproj は変えない）。ビルドとテストは最初から `dotnet build|test app/PartsTray.sln --no-restore --disable-build-servers "-p:TargetPlatformSdkPath=C:/Program Files (x86)/Windows Kits/10" -p:TargetPlatformDisplayName=Windows`
- 最後のメッセージ（日本語）：変えたこと、テスト件数、見本の場所、config.json に書くときの値の例
