指示書 S の実装・テスト・説明更新を完了しました。

1. **変更ファイルと内容**

   以下はすべて `app/` 内です。

   | ファイル | 変更内容 |
   |---|---|
   | `PartsTray.Core/Monitor.cs`・`AcpiTemperatureHealth.cs` | 20℃ 未満の除外、固定温度の判定・復帰、通知の重複防止 |
   | `PartsTray/MonitorModule.cs`・`MonitorModule.Temperature.cs` | 温度判定の接続、「--」表示・案内・通知 |
   | `PartsTray.Core/SettingsNavigation.cs` | アイコンからタブ・欄を決める純粋関数 |
   | `PartsTray/Program.cs`・`TrayContext.Settings.cs`・`BatteryModule.cs` | クリック元の受け渡し、設定表示、時間計測 |
   | `PartsTray/SettingsWindow.Navigation.cs` | 該当タブ・欄・機器への移動 |
   | `PartsTray/SettingsWindow.cs`・`SettingsWindow.Loading.cs`・`SettingsWindow.BatteryKinds.cs` | 表示後の非同期読み込み、編集・保存状態の保持 |
   | `PartsTray.Core/Settings.cs`・`SettingsText.cs` | 背景色の既定値・検証・保存 |
   | `PartsTray.Core/Palette.cs`・`PartsTray/MonitorModule.Drawing.cs` | 背景描画、文字のコントラスト補正 |
   | `PartsTray/SettingsWindow.Icons.cs`・`SettingsWindow.Colors.cs` | 背景色の入力・色選択・透明への復帰 |
   | `PartsTray.Tests/AcpiTemperatureHealthTests.cs`・`SettingsNavigationTests.cs`・`MonitorBackgroundTests.cs` | 追加テスト |
   | [README.md](../../app/README.md)・[改造の手引き.md](../../app/改造の手引き.md) | 利用方法・設定・編集先の説明 |

2. **テスト・ビルド**

   **全 911 件合格（既存 859 件＋追加 52 件）、失敗 0、スキップ 0。ビルドは警告 0・エラー 0。**

   SDK 探索先のアクセス拒否には、許可された一時引数で対応しました。

3. **判断したこと・理由**

   固定温度は「5 分以上・CPU 使用率差 30 ポイント以上」で判定し、温度が変われば復帰します。記録・通知は起動中に一度だけです。

   電池一覧は取得済みデータを利用します。背景が透明なら従来の描画・補正を維持し、上下表示では各半分に背景色を適用します。

   既存の未コミット変更と改行形式を保持しました。別途更新された `GestureModule.Release.cs` も戻していません。禁止対象の変更、アプリ・機器操作、schtasks、レジストリ書換え、昇格、ダウンロード、git による書換えは実行していません。

4. **実機で確かめてほしいこと**

   温度の固定判定・復帰・通知一回、各アイコンからの設定移動、既存画面の前面化、読み込み途中の適用・キャンセル・終了、表示時間のログ、明暗テーマと上下表示での背景色・透明への復帰を確認してください。