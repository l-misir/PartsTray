`monitor.hundredStyle` の既定値を `uniform` にしました。**20ドットの「100」では「1」だけが右に1ドット移り、「1と0」「0と0」の間がどちらも1ドットになりました。** 細字はどの数字の組み合わせでも字間を1ドットにそろえ、数字列を左右中央に置きます。温度・メモリの2段表示とオウムのCPU表示にも適用しました。

**[変更前後の見本を開く](<../../codex/見本/数字100/uniform_詰めた並べ方.png>)**  
温度の範囲で「8・37・99・100・150」を並べました。20・16ドットそれぞれに暗い背景と明るい背景があり、各行の左が実寸、右が8倍です。

### 大きさの決め方と実測結果

CPU・メモリは0〜100、温度は0〜150の全値を測り、それぞれの枠に収まる最大の文字サイズを0.25px刻みで選びました。どの値も同じサイズを使うので、99から100に変わっても字は小さくなりません。

| アイコンの一辺 | 変更前の「100」の高さ | 変更後のCPU・メモリ | 変更後の温度 |
|---|---:|---:|---:|
| 16ドット | 6 | 6 | 6 |
| 20ドット | 8 | 8 | 8 |
| 24ドット | 9 | 10 | 10 |
| 32ドット | 12 | 14 | 13 |

表は単独表示で実際に色が付いた高さ（ドット）です。16・20ドットでは字間が整い、24・32ドットでは数字も大きくなりました。

細字の `uniform` は Tahoma Bold、太字は従来のドット絵を使います。明示設定では、全体を縮める `shrink`、横幅を詰める `narrow`、「1」を縦線にして詰める `narrowOne` も選べます。

### 検証結果

- 指定コマンドでビルド成功。警告0件、エラー0件。テストは**追加32件を含む全1,080件が合格**しました（失敗0件、スキップ0件）。
- 実フォントの描画検証は**全15,093件合格**しました。全表示範囲の収まり、字間1ドット、桁数が変わっても同じ字形を使うことを確認しました。
- 見本は全40組で、実寸の各画素と8倍側の対応する8×8画素が一致しました。

### 変更したファイル

- 描画　[PackedDigits.cs](<../../app/PartsTray.Core/PackedDigits.cs>)、[DigitArt.cs](<../../app/PartsTray/DigitArt.cs>)、[MonitorModule.Drawing.cs](<../../app/PartsTray/MonitorModule.Drawing.cs>)
- 設定・説明　[Settings.cs](<../../app/PartsTray.Core/Settings.cs>)、[README.md](<../../app/README.md>)
- テスト　[PackedDigitsTests.cs](<../../app/PartsTray.Tests/PackedDigitsTests.cs>)、[MonitorSettingsTests.cs](<../../app/PartsTray.Tests/MonitorSettingsTests.cs>)
- 見本　[Program.cs](<../../codex/見本/数字100/Program.cs>)、[UniformComparison.cs](<../../codex/見本/数字100/UniformComparison.cs>)、[MaskChecks.cs](<../../codex/見本/数字100/MaskChecks.cs>)、[Sample.csproj](<../../codex/見本/数字100/Sample.csproj>)、上記の比較PNG