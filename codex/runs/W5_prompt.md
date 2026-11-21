`codex/指示書W_100の数字の大きさ.md` の続き（5 回目）です。前回、Windows に入っているフォント 10 種の見本（`codex/見本/数字100/フォント比べ.png`）を作りました。

ユーザー（11/21）：「PixelMplus とか Terminus 系はどうですか？」。ユーザーの許可を得て、Claude が `codex/見本/フォント/` にダウンロードして展開しました（**インストールはしていない**）：
- `codex/見本/フォント/PixelMplus/PixelMplus-20130602/PixelMplus10-Regular.ttf`・`PixelMplus10-Bold.ttf`・`PixelMplus12-Regular.ttf`・`PixelMplus12-Bold.ttf`（M+ FONT LICENSE）
- `codex/見本/フォント/Terminus/terminus-ttf-4.49.3/TerminusTTF-4.49.3.ttf`・`TerminusTTF-Bold-4.49.3.ttf`（SIL OFL 1.1）

やること（**見本だけ。`app/` のコードは変えない**）：
- 見本を描くプログラム（`codex/見本/数字100/`）で、これらのフォントをファイルから読み込んで（`PrivateFontCollection` など。PC にインストールしない）、`フォント比べ.png` と同じ並べ方の `ドットフォント比べ.png` を書き出す
- ドットフォントは**決まった大きさでだけ**くっきりする。アンチエイリアスを切り（`TextRenderingHint.SingleBitPerPixelGridFit` など）、**そのフォント本来の大きさ**（PixelMplus10 は 10px、PixelMplus12 は 12px、Terminus は 12〜32px のうちの整数の大きさ）で描いて、拡大縮小しない。アイコンの大きさ（16・20・24・32）ごとに、`uniform`（0〜150 が字の間 1 ドットで枠に収まる）の決まりで入る**いちばん大きい本来の大きさ**を選ぶ。どれも入らない大きさは「入らない」と書く
- 行：PixelMplus10（Regular・Bold）、PixelMplus12（Regular・Bold）、Terminus（Regular・Bold）、比べるために Tahoma Bold（今）・Bahnschrift SemiBold・Segoe UI Bold。列は前回と同じ（「0123456789」と「8・37・55・99・100・150」、20 と 16 ドット、暗い背景、20 ドットの 8 倍）。24・32 ドットの実寸も小さく添える
- 形が似すぎる数字の印（▲）も前回と同じ決まりで
- 決まり：`codex/見本/` の外を変えない、`app/` は読むだけ（見本のプログラムが `app/` の描き方を使うのはよい）、`rust/` に触れない、アプリを起動しない、ダウンロードしない、`git` で書き換えない。見本のプログラムは動かしてよい。ビルドが要るときは `--disable-build-servers "-p:TargetPlatformSdkPath=C:/Program Files (x86)/Windows Kits/10" -p:TargetPlatformDisplayName=Windows` を付ける
- 最後のメッセージ（日本語）：見本の場所、フォントごとに選んだ本来の大きさ、気づいたこと（5 の形・16 ドットでの区別など）、同梱するときのライセンスの注意
