`monitor.hundredStyle` で「100」の描き方を3案から選べるようにしました。設定画面には表示せず、既定値は `shrink` です。温度・メモリ・CPU、温度とメモリの2段表示、オウムに重ねるCPU表示に共通で適用されます。

**見本画像**　[数字100_見本.png を開く](<../../codex/見本/数字100/数字100_見本.png>)  
上部に実寸、下部に8倍拡大を並べました。お使いの設定は「細字」の行です。`99` のすぐ下に `100` があり、高さの変化を比較できます。

| 設定値 | 100の見え方 |
|---|---|
| `shrink`（既定） | 従来の描き方です。単独表示の細字は、幅に合わせて数字全体を小さくします。 |
| `narrow` | 1・2桁と同じ高さを保ち、横幅と字間を詰めて3桁を描きます。 |
| `narrowOne` | 「1」を細い縦線にし、「0」は元の高さと形を保ちます。字間をゼロにしても収まらない場合は `narrow` と同じ描き方になります。 |

**迷った点**　従来の太字・16ドットの単独表示では、`100` が枠に収まらず `99` と表示されます。`shrink` は既定の見え方を保つため、この挙動も維持して見本に注記しました。ほかの2案では同じ枠に `100` を表示します。

**検証結果**　ビルドは警告0件・エラー0件。テストは **1,011件すべて合格**（失敗0件・スキップ0件）です。見本生成時の描画検証も648件すべて合格し、実寸と8倍拡大の全192組で画素が一致しました。アプリ本体の起動・機器操作・ダウンロードは行っていません。

**変更ファイル**

- 設定と保存処理　[Settings.cs](<../../app/PartsTray.Core/Settings.cs>)、[SettingsText.cs](<../../app/PartsTray.Core/SettingsText.cs>)
- 数字の描画　[HundredDigits.cs](<../../app/PartsTray.Core/HundredDigits.cs>)、[DigitArt.cs](<../../app/PartsTray/DigitArt.cs>)、[MonitorModule.cs](<../../app/PartsTray/MonitorModule.cs>)、[MonitorModule.Drawing.cs](<../../app/PartsTray/MonitorModule.Drawing.cs>)
- テスト　[HundredDigitsTests.cs](<../../app/PartsTray.Tests/HundredDigitsTests.cs>)、[MonitorSettingsTests.cs](<../../app/PartsTray.Tests/MonitorSettingsTests.cs>)
- 見本　[Sample.csproj](<../../codex/見本/数字100/Sample.csproj>)、[Program.cs](<../../codex/見本/数字100/Program.cs>)、[MaskChecks.cs](<../../codex/見本/数字100/MaskChecks.cs>)、[数字100_見本.png](<../../codex/見本/数字100/数字100_見本.png>)