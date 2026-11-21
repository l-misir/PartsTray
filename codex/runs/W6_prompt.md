`codex/指示書W_100の数字の大きさ.md` の続き（6 回目）です。前回 `codex/見本/数字100/ドットフォント比べ.png` を作りました。

ユーザー（11/21）：「好みは Terminus Bold です」。ただ 20 ドットの枠では Bold は 12 px までしか入らず、Regular なら 16 px 入る。**実機の通知領域で見比べることになりました**。

やること：
1. **Terminus を PartsTray に入れる**：`codex/見本/フォント/Terminus/terminus-ttf-4.49.3/` の `TerminusTTF-4.49.3.ttf` と `TerminusTTF-Bold-4.49.3.ttf` を `app/PartsTray/Fonts/` に写し、アプリの中に埋め込む（`EmbeddedResource`）。ライセンス（SIL OFL 1.1）の全文 `COPYING` を `app/PartsTray/Fonts/Terminus-LICENSE.txt` として写し、**publish の出力にも入るように**する（配る zip に入る）。**この 2 つのために `app/PartsTray/PartsTray.csproj` に行を足すことだけは許す**（ほかの変更、特に `TargetPlatformSdkPath` などは足さない）
2. **設定 `monitor.digitFont` で選べるようにする**：値 `Terminus`（Regular）と `TerminusBold` を足す（今の Windows のフォント名の指定はそのまま使える）。読み込みは埋め込みから（`PrivateFontCollection.AddMemoryFont` など。PC にインストールしない）。ドットフォントは**本来の大きさで、ぼかしなし**（`SingleBitPerPixelGridFit` など）で描き、拡大縮小しない。アイコンの大きさごとに、`uniform`（0〜150 が字の間 1 ドットで収まる）で入る**いちばん大きい本来の大きさ**を使う（前回の見本の表と同じ結果になること：Regular は 16→12・20→16・24→20・32→28、Bold は 16→12・20→12・24→20・32→28）。2 段の表示・オウムに重ねる数字も同じ
3. 既定はまだ変えない（Claude が実機で切り替えて見比べてもらう）
4. `app/README.md` に、同梱したフォントとライセンスのことを書く。`CHANGELOG.md`・`README.md`（外）は変えない（Claude が書く）
5. テスト：`TerminusBold`・`Terminus` の読み込み、大きさの選び方（上の表）、範囲の数が収まる
- 決まり（§0）は前回と同じ（`rust/` に触れない、アプリを起動しない、ダウンロードしない、`git` で書き換えない）。ビルドとテストは最初から `dotnet build|test app/PartsTray.sln --no-restore --disable-build-servers "-p:TargetPlatformSdkPath=C:/Program Files (x86)/Windows Kits/10" -p:TargetPlatformDisplayName=Windows`
- 最後のメッセージ（日本語）：変えたこと、テスト件数、csproj に足した行
