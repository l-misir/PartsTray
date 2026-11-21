**[10書体の見本「フォント比べ.png」を開く](<../../codex/見本/数字100/フォント比べ.png>)**

細字のフォントを `monitor.digitFont` で選べるようにしました。既定は `Tahoma`（Tahoma Bold）です。指定したフォントが PC にない場合は Tahoma で描画します。C# 版では `config.json` で指定でき、設定画面で別の項目を保存してもフォント名は保持されます。

### 「5」の見比べ

今の「5」の印象を変えるなら、まず **Segoe UI Semibold** と **Bahnschrift SemiBold** の「55」列を見てください。前者は上の横棒が1ドット厚です。後者は下半分の丸みが大きく、16ドットでも「5」の高さが9ドットあります。見本は左が20・16ドットの実寸、右が20ドットの8倍です。

| 書体 | 20ドット枠の「5」<br>幅×高さ | 見比べた結果 |
|---|---:|---|
| Tahoma Bold | 6×8 | 現在の字形。16ドットでは5×6 |
| Segoe UI Bold | 5×10 | 縦長で、上の横棒は2ドット厚 |
| Segoe UI Semibold | 5×9 | 上の横棒は1ドット厚。16ドットでは Bold と同じ字形 |
| Verdana Bold | 6×8 | 20ドットの「5」は Tahoma と全画素が一致。16ドットでは1ドット細い |
| Arial Bold | 7×10 | 横幅が広く、下の曲線も太い |
| Bahnschrift SemiBold | 7×11 | 下部の丸みが見える。16ドットでは5×9 |
| Bahnschrift Bold | 7×11 | 20ドットでは SemiBold より中央の横棒が1画素長い |
| Consolas Bold | 5×7 | コンパクト。16ドットでは「3」と「5」が近い |
| Yu Gothic UI Bold | 5×9 | 16ドットでは幅3ドットで、候補中もっとも細い |
| Meiryo UI Bold | 4×7 | 16・20ドットで「5」の形と大きさが同じ |

**▲は、数字の穴が消えるか、別の数字と重ねたときの差が3画素以下になる場合**に付けました。16ドットの Arial Bold は「6と8」「8と9」が各2画素差、「3と8」「3と9」が各3画素差でした。Consolas Bold は「3と5」が3画素差でした。20ドットでは該当する組はなく、両サイズとも全書体で「0・6・8・9」の穴が残りました。

### テスト結果

- 指定コマンドでビルド成功。警告0件、エラー0件。テストは**全1,120件合格**（失敗0、スキップ0）。
- 実フォントの描画検証は**20,271件すべて合格**。未導入フォントと Tahoma の描画が一致し、全10書体で0〜150の全値が16・20・24・32ドットの単独・2段表示の枠に収まりました。字間も1ドットです。
- 従来の描画検証は**15,093件すべて合格**。保存したPNGも、数字列と表示例の全70組で実寸の1画素と拡大側の8×8画素が一致しました。

### 変更したファイル

- 設定と説明　[Settings.cs](<../../app/PartsTray.Core/Settings.cs>)、[SettingsText.cs](<../../app/PartsTray.Core/SettingsText.cs>)、[README.md](<../../app/README.md>)
- 描画　[DigitArt.cs](<../../app/PartsTray/DigitArt.cs>)、[MonitorModule.Drawing.cs](<../../app/PartsTray/MonitorModule.Drawing.cs>)、[MonitorModule.cs](<../../app/PartsTray/MonitorModule.cs>)
- テスト　[MonitorSettingsTests.cs](<../../app/PartsTray.Tests/MonitorSettingsTests.cs>)、[MaskChecks.cs](<../../codex/見本/数字100/MaskChecks.cs>)
- 見本　[FontComparison.cs](<../../codex/見本/数字100/FontComparison.cs>)、[Program.cs](<../../codex/見本/数字100/Program.cs>)、[UniformComparison.cs](<../../codex/見本/数字100/UniformComparison.cs>)、[Sample.csproj](<../../codex/見本/数字100/Sample.csproj>)、[フォント比べ.png](<../../codex/見本/数字100/フォント比べ.png>)