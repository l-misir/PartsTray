`codex/指示書W_100の数字の大きさ.md` の続き（9 回目）です。前回（W8）で `"Tahoma, Regular, Pixel"`（Core Temp と同じ描き方）を足し、実機に入りました。

ユーザー（11/21）：「Tahoma 標準・にじみなしで、2 桁の時の横幅（隙間含めて）と縦の幅を同じにできない？ ちょっと縦に潰れてるんだよね…」。**ユーザーは Tahoma 標準・にじみなしに決めた**。

やること：
1. **字を縦長にする指定を足す**：フォントの指定の「名前, 太さ, 描き方」に、4 つ目として `Square2` を書けるようにする（例 `"Tahoma, Regular, Pixel, Square2"`）。`Square2` のとき、**2 桁の数（字の間 1 ドットを含む）の横幅と、字の高さが同じ（正方形）になる**ように、字の幅を狭めて高さを上げる。GDI の `LOGFONT` の `lfHeight` と `lfWidth`（字の幅を狭める）で、にじみなし（`NONANTIALIASED_QUALITY`）のまま描く。GDI+ で同じことができるならそれでもよい
   - 大きさの決め方：`uniform`（1〜3 桁で同じ大きさ、0〜150 が字の間 1 ドットで枠に収まる）の中で、2 桁の幅＝高さ にできる**いちばん大きい**組み合わせ。ぴったり同じにできないときは、2 桁の幅が高さより 1 ドット小さいほうに合わせる
   - 2 段の表示（温度とメモリ）は、段の高さに合わせて同じ決まりで（2 段では横長のままでもよい。どうしたか報告に書く）
2. **見本** `codex/見本/数字100/縦長の比べ.png`：行 ①今の `"Tahoma, Regular, Pixel"` ②`"Tahoma, Regular, Pixel, Square2"` ③Core Temp と同じ（Tahoma 標準 8）。列「0・8・37・55・99・100・150」、20 と 16 ドット、24 と 32 ドット（実寸だけでよい）、暗い・明るい背景、実寸と 8 倍。行ごとに字の幅×高さ（ドット）を書く
3. テスト：`Square2` の読み方、2 桁の幅と高さの関係、範囲の数が収まる
- 既定はまだ変えない（ユーザーが見本で決めたら Claude が設定する）
- 決まり（§0）は前回と同じ（`rust/` に触れない、アプリを起動しない、ダウンロードしない、`git` で書き換えない、csproj は変えない）。ビルドとテストは最初から `dotnet build|test app/PartsTray.sln --no-restore --disable-build-servers "-p:TargetPlatformSdkPath=C:/Program Files (x86)/Windows Kits/10" -p:TargetPlatformDisplayName=Windows`
- 最後のメッセージ（日本語）：変えたこと、テスト件数、見本の場所、大きさごとの字の幅×高さ、config.json の値の例
