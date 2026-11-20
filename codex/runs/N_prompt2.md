前回は使用量の上限で、指示書N の §2（ファイルの分け方）の途中で止まりました。止まった時点では、`app/LogiGap/TrayIconSlot.cs` の 41 行目でビルドエラー（CS7042：ジェネリックの型の中に DllImport がある）が出ています。

`codex/指示書N_誰でもそのまま使えるようにする.md` の続きを、最後まで実装してください。

- まず今のファイルの状態（`git status`・`git diff` と、新しく作ったファイル）を確かめ、§2 を仕上げてビルドとテストを通してから、§3 → §4 → §5 → テスト（§6）→ README（§7）の順に進めてください
- 前回の決まり（`codex/runs/N_prompt.md`）はそのまま守ってください：`app/` の外を変えない、`app/publish/`・`logs/`・`app/LogiGap/Assets/` に触れない、アプリや機器を動かすプログラムを起動しない、ダウンロードしない、`git` で書き換えない、パッケージや環境変数を変えない
- §4.3 の新しい絵（イヤホン・ゲームパッド・その他）と、**キーボードの絵の描き直し**は、止まっている間にユーザーと見本を見比べて形が決まりました。**下の「決まった絵の形」のとおりに**描いてください（指示書の §4.3 の「くっきり／滑らか」の判断と「今の絵は変えない」より、こちらを優先。§0.3・§5.4 の「暗い背景で 1 ドットも変えない」も、キーボードの絵だけは例外）
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書N §9 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください

## 決まった絵の形（11/20 ユーザーの指定）

座標の決まりは今の絵と同じです：`a` は絵の箱（4 倍の大きさで描くときの座標。`s = size * 4`、`thick = s / 10f`、`inner = s - 2 * (thick + s / 10f)`、`a = new RectangleF((s - inner) / 2, (s - inner) / 2, inner, inner)`）。`k = 4`。輪は今のまま（どの機器も滑らか）。`size` はアイコンの一辺（125% なら 20）。以下の「1 倍の列」は、`size` が偶数の前提（16・20・24・32）。

### イヤホン：AirPods のような軸付きを左右に 2 つ。全体は滑らか、左右の間の空白（2 列）だけくっきり

ユーザーの言葉：「右耳用と左耳用の間の空白部分をはっきりさせると、霞まずにイヤホンとわかりやすくなる」「真ん中で区切ったときに線対称になってるほう」。

```csharp
float W = a.Width, H = a.Height, headW = W * 0.46f, headH = H * 0.40f, stemW = W * 0.19f, hy = a.Y + H * 0.02f;
int center = size / 2;                               // 20px なら 10（列 9 と 10 の境目）
float leftInner = (center - 1) * k, rightInner = (center + 1) * k;   // 1 倍で、左のイヤホンの内側の端が列 9 の左端、右が列 11 の左端
float top = hy + headH * 0.45f;
// 左：頭は内側の端から左へ、軸は内側の端に沿って下へ
g.FillEllipse(white, leftInner - headW, hy, headW, headH);
g.FillPath(white, Rounded(new RectangleF(leftInner - stemW, top, stemW, a.Bottom - top), stemW / 2));
// 右：左右対称
g.FillEllipse(white, rightInner, hy, headW, headH);
g.FillPath(white, Rounded(new RectangleF(rightInner, top, stemW, a.Bottom - top), stemW / 2));
// ここまでを 4 倍で描いて滑らかに縮めた後、1 倍の画像で：
//   列 center-1 と center（20px なら 9 と 10）を全部の行で透明にする（空白）
//   その両隣の列 center-2 と center+1（8 と 11）は、点を 0/1 に分ける（不透明度 128 以上を不透明、それ以外を透明）
```

### ゲームパッド：くっきり。本体と左右の握り。左にスティック（2×2 の四角い穴）、右にひし形の頂点に並んだボタン 4 つ

本体と握りを 4 倍で描いて縮め、点を 0/1 に分けた後で、**1 倍の画像の点を直接**透明にして穴を抜く（4 倍で穴を描くと、縮めたときに消えたり潰れたりするため）。

```csharp
float W = a.Width, H = a.Height;
g.FillPath(white, Rounded(new RectangleF(a.X, a.Y + H * 0.18f, W, H * 0.50f), H * 0.22f));                         // 本体
g.FillPath(white, Rounded(new RectangleF(a.X, a.Y + H * 0.30f, W * 0.38f, H * 0.62f), W * 0.19f));                 // 左の握り
g.FillPath(white, Rounded(new RectangleF(a.Right - W * 0.38f, a.Y + H * 0.30f, W * 0.38f, H * 0.62f), W * 0.19f)); // 右の握り
// ここまでを縮めて 0/1 に分けた後、1 倍の座標で穴を抜く
float box = size - 2 * (size / 10f + size / 10f), bx = (size - box) / 2;   // 1 倍の絵の箱（20px なら 12、左上 4）
int cy = (int)Math.Round(bx + box * 0.42f), arm = Math.Max(1, (int)Math.Round(box / 12f));
int dx = (int)Math.Round(bx + box * 0.25f), ax = (int)Math.Round(bx + box * 0.72f);
// スティック（左）：一辺 2*arm の四角い穴。左上は (dx - arm, cy - arm + 1)。20px なら横 6〜7・縦 9〜10
for (int y = 0; y < 2 * arm; y++) for (int x = 0; x < 2 * arm; x++) Hole(dx - arm + x, cy - arm + 1 + y);
// ボタン 4 つ（右、ひし形の頂点）。20px なら (13,8) (14,9) (13,10) (12,9)
Hole(ax, cy - arm); Hole(ax + arm, cy); Hole(ax, cy + arm); Hole(ax - arm, cy);
```

### キーボード：今の絵を変える。くっきり。キーの粒 2 段（2 段目を 1 ドット右にずらす）と、下にスペースキーの横棒

ユーザーの言葉：「キーボードの下の方の凸型のやつはただの横棒（下側のみ）にして、粒を増やしたほうがいい」「穴部分を全体的に 1 ドット下げて」。
本体（今と同じ角の丸い横長の四角）だけを 4 倍で描いて縮め、点を 0/1 に分けた後で、**1 倍の画像の点を直接**透明にして穴を抜く。

```csharp
float h = a.Height * 0.66f; var body = new RectangleF(a.X, a.Y + (a.Height - h) / 2, a.Width, h);
g.FillPath(white, Rounded(body, a.Width * 0.14f));          // 本体（穴はここでは描かない）
// 縮めて 0/1 に分けた後、1 倍の画像で本体の上下左右の端の行・列を調べる
//   top・bottom：真ん中の列（size / 2）で不透明な一番上と一番下の行
//   left・right：(top + bottom) / 2 の行で不透明な一番左と一番右の列
// 20px なら本体は 横 4〜15・縦 6〜13
// 穴は本体の内側（left < x < right かつ top < y < bottom）だけに抜く
int y0 = top + 2;                                           // 1 段目の粒の行（20px なら 8）
for (int x = left + 1; x < right; x += 2) Hole(x, y0);       // 1 段目：20px なら 5,7,9,11,13
for (int x = left + 2; x < right; x += 2) Hole(x, y0 + 2);   // 2 段目（1 ドット右へずらす）：6,8,10,12,14
int sy = y0 + 4, sx = size / 2 - 3;                          // スペースキーの横棒：長さ 6。20px なら 横 7〜12・縦 12
for (int x = sx; x < sx + 6; x++) Hole(x, sy);
```

`size` が 20 以外（16・24・32）でも同じ式で描く。粒と横棒が本体からはみ出す場合は、はみ出す分を抜かない。

### その他：くっきり。横向きの電池（塗りつぶし）

```csharp
float W = a.Width, H = a.Height;
g.FillPath(white, Rounded(new RectangleF(a.X, a.Y + H * 0.25f, W * 0.86f, H * 0.50f), H * 0.10f));   // 本体
g.FillRectangle(white, a.X + W * 0.88f, a.Y + H * 0.40f, W * 0.12f, H * 0.20f);                     // 右の出っ張り
```

明るいテーマでは、白の代わりに §5 の色（背景とのコントラスト 3:1 以上）で描く。穴（透明）はテーマに関係なく透明のまま。
