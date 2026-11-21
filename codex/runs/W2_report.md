`monitor.hundredStyle` に4つ目の案 `uniform` を追加しました。**単独表示、温度・メモリの2段表示、オウムのCPU表示のすべてで、99から100に変わっても1文字の大きさがそろいます。** 既定値は `shrink` のままです。

### 見本の画像

- [細字の4案.png](<../../codex/見本/数字100/細字の4案.png>) — 20ドット・暗い背景で「8・37・99・100」を並べました。各行の左が実寸、右が8倍です。
- [数字100_見本.png](<../../codex/見本/数字100/数字100_見本.png>) — 4案すべてを、16・20・24・32ドット、暗い背景と明るい背景、細字と太字で比較できます。

### 案ごとの違いと判断

| 案 | 描き方 | 20ドットの細字の実測高（99 → 100） |
|---|---|---|
| `shrink`（既定） | 3桁が入らないときに全体を縮める従来の表示 | 14 → 10ドット |
| `narrow` | 高さを保ち、3桁の横幅を詰める | 14 → 14ドット |
| `narrowOne` | 「1」を縦線にして字間を詰める。収まらない場合は `narrow` と同じ描き方を使う | 14 → 14ドット |
| `uniform` | 1〜3桁を共通サイズにそろえる。細字には太い字形を使い、太字は従来のドット絵を使う | **8 → 8ドット** |

`uniform` の細字には **Tahoma Bold** を選びました。「100」に加え、左右に最も張り出す3桁も測ってサイズを決めています。20ドットの見本では「8・37・99・100」のすべてが高さ8ドットです。

### ビルドとテスト

- ビルドは警告0件・エラー0件。テストは**追加5件を含む全1,035件が合格**しました。失敗・スキップは0件です。
- 見本生成時の描画検証も**全24,649件が合格**しました。`uniform` は0〜999の全値で枠内に収まることを確認しました。両PNGとも、実寸の各画素と8倍側の対応する8×8画素が一致しています。

### 変更したファイル

- **設定・ドット字形**　[Settings.cs](<../../app/PartsTray.Core/Settings.cs>)、[Monitor.cs](<../../app/PartsTray.Core/Monitor.cs>)、[HundredDigits.cs](<../../app/PartsTray.Core/HundredDigits.cs>)
- **細字描画・オウムの配置**　[DigitArt.cs](<../../app/PartsTray/DigitArt.cs>)、[MonitorModule.Drawing.cs](<../../app/PartsTray/MonitorModule.Drawing.cs>)
- **テスト**　[HundredDigitsTests.cs](<../../app/PartsTray.Tests/HundredDigitsTests.cs>)、[MonitorSettingsTests.cs](<../../app/PartsTray.Tests/MonitorSettingsTests.cs>)
- **見本**　[Program.cs](<../../codex/見本/数字100/Program.cs>)、[MaskChecks.cs](<../../codex/見本/数字100/MaskChecks.cs>)、上記のPNG 2枚。