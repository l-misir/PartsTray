指示書Nの実装を完了しました。[README](../../app/README.md)にも追記しています。

1. **ファイルの分け方**

   `TrayIconSlot` のDllImportをジェネリック型の外へ移し、CS7042を解消しました。分離後の主なファイルは次のとおりです（`app/` 基準）。

   | ファイル | 役目 | 行数 |
   |---|---|---:|
   | LogiGap/BatteryModule.cs | 機器一覧・更新・説明文 | 150 |
   | LogiGap/BatteryIcon.cs | 輪・稲妻・縮小・薄さ | 119 |
   | LogiGap/DevicePictures.cs | 機器の絵と穴の仕上げ | 156 |
   | LogiGap/BluetoothBatteryReader.cs | Bluetoothの情報取得 | 147 |
   | LogiGap/TrayIconSlot.cs | アイコンとハンドルの管理 | 48 |
   | LogiGap/MonitorModule.cs | 取得・タイマー・表示切替 | 288 |
   | LogiGap/MonitorModule.Drawing.cs | 数字とオウムの描画 | 122 |
   | LogiGap/ParrotFrames.cs | コマの読込・加工・縮小 | 156 |
   | LogiGap/AcpiTemperature.cs | ACPI温度の取得 | 51 |
   | LogiGap/TaskbarTheme.cs | 明暗テーマの監視 | 38 |
   | LogiGap.Core/Battery.cs | 電池の型・解釈・説明文 | 70 |
   | LogiGap.Core/LogitechBatteryTracker.cs | 電池の読取専用追跡 | 283 |
   | LogiGap.Core/BatteryKinds.cs | 種類判定 | 70 |
   | LogiGap.Core/BatteryLayout.cs | 12枠への割り当て | 33 |
   | LogiGap.Core/Palette.cs | 色とコントラスト補正 | 74 |
   | LogiGap/SettingsWindow.BatteryKinds.cs | 種類の設定表 | 52 |

2. **肩代わりを切ったときの動き**

   既定を無効にしました。既存の `enabled=true` は維持します。無効時も受信機と電池読取は継続し、肩代わりの送信は停止します。

   MX Master 4はWPID通知と既存の識別・電池照会結果から記憶し、追加要求は送りません。設定タブは有効時または検出後だけ表示し、非表示の設定値は保持します。

3. **種類判定と絵**

   設定 → HID → Class of Device → 名前 → その他の順で判定します。イヤホン・ゲームパッド・その他・キーボードは今回指定の形を実装しました。イヤホンは滑らか＋中央の空白を明確化し、残りはくっきり描画。穴は縮小後に抜きます。

   電池枠は11〜22。既存のマウス11・キーボード12・ヘッドホン13を維持し、動的なウィンドウ用アイコンは23以降です。

4. **明暗テーマと見た目の確認**

   5秒ごとにテーマを読み、色相・彩度を保って明度を補正し、基本描画色のコントラストを3:1以上にします。手動指定にも対応しました。

   [描画検証](../../app/LogiGap.Tests/VerifyDrawing.py)で、暗いテーマのマウス・ヘッドホン・数字を旧コードと比較し、**7,600ケースで画素が完全一致**しました。指定変更のキーボードは一致対象外です。新しい絵も4サイズ×明暗の**8ケース合格**です。

5. **追加した `// 要確認:`**

   - `LogitechBatteryTracker.cs`：起動時の完全一致名による識別。
   - `BatteryKinds.cs`：複数HID種類がある場合の優先順。
   - `BatteryLayout.cs`：同名機器をID順で固定。
   - `SettingsWindow.BatteryKinds.cs`：同名機器を1行にまとめる扱い。
   - `Palette.cs`：未実測の明るい背景色、明度補正の丸め・停止条件。
   - `TaskbarTheme.cs`：取得失敗時は直前の明暗を維持。
   - `DevicePictures.cs`：縮小時の左右の不透明度差をなくすため、イヤホンを鏡映して画素でも左右対称にする処理。

   従来の確認コメントも維持しています。

6. **ビルド・テスト結果**

   - §2完了時：既存 **549件すべて合格**。
   - `dotnet build app/LogiGap.sln --no-restore`：**成功、警告0・エラー0**。
   - `dotnet test app/LogiGap.sln --no-restore`：**636件すべて合格、失敗0・スキップ0**。
   - `git diff --check`：問題なし。

7. **実機で確認してほしいこと**

   肩代わり無効時の電池表示、検出後の設定タブ、Bluetoothの種類と切断表示、各倍率での絵、明暗切替とオウムのふち、アイコン番号・並びを確認してください。

   アプリ本体・HID・フック・入力送信・通知・トレイ・ウィンドウは起動していません。禁止された場所の変更、ダウンロード、パッケージ・環境変数の変更、Gitによる書き換えも行っていません。