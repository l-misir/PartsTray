`codex/指示書W_100の数字の大きさ.md` の続き（7 回目）です。前回 Terminus（`Terminus`・`TerminusBold`）を `monitor.digitFont` で選べるようにし、実機で `TerminusBold` にしました。

ユーザー（11/21）：「CPU 使用率は今の（またはおすすめのやつ）、CPU 温度は Regular、メモリ使用率は Bold で並べることはできない？ ちなみに Bold は流石に小さいですね…」

やること：
1. **アイコンごとにフォントを変えられるようにする**：`monitor.cpuDigitFont`・`monitor.temperatureDigitFont`・`monitor.memoryDigitFont`（既定は空＝`monitor.digitFont` を使う）。2 段の表示（温度とメモリ）は、それぞれの段にそれぞれのフォント。オウムに重ねる CPU の数字は `cpuDigitFont`
2. 大きさの選び方（`uniform`・字の間 1 ドット・0〜150 が収まる・ドットフォントは本来の大きさ）は、アイコンごとに、そのフォントで今までどおり決める
3. 設定の値として書けるフォントの名前（`Tahoma`・`Bahnschrift SemiBold` など Windows のもの、`Terminus`・`TerminusBold`）を、完了報告にそのまま書く（Claude が config.json に書くため）。特に **Bahnschrift SemiBold を指定するときの正しい値**
4. テスト：アイコンごとの指定が既定より優先される、空なら `digitFont`、2 段の表示
- 決まり（§0）は前回と同じ（`rust/` に触れない、アプリを起動しない、ダウンロードしない、`git` で書き換えない）。csproj は変えない。ビルドとテストは最初から `dotnet build|test app/PartsTray.sln --no-restore --disable-build-servers "-p:TargetPlatformSdkPath=C:/Program Files (x86)/Windows Kits/10" -p:TargetPlatformDisplayName=Windows`
- 最後のメッセージ（日本語）：変えたこと、テスト件数、書けるフォントの名前の一覧
