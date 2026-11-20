1. 変更したファイルと内容  
   すべて `app/` 内です。
   - `LogiGap.Core/Settings.cs`：滑らか化・記録設定と検証を追加。
   - `LogiGap.Core/Protocol.cs`：`WheelSmoother` を追加。
   - `LogiGap.Core/GapSession.cs`：分割送信、残量管理、待機期限、受信・送信集計を実装。
   - `LogiGap.Core/WheelTrace.cs`：送り元の分類と記録形式を追加。
   - `LogiGap/GestureModule.cs`：横スクロール記録と設定・終了処理を接続。
   - `LogiGap/GapModule.cs`：高精度待機タイマーと失敗時の通常待機を追加。
   - `LogiGap/WheelTraceWriter.cs`：背景書き込み、件数上限、停止時の書き出しを実装。
   - `LogiGap.Tests/NativeWheelTests.cs`：既存テストの設定を明示し、分割送信の検証を追加。
   - `LogiGap.Tests/WheelSmootherTests.cs`：分割・反転・遅延・リセットを検証。
   - `LogiGap.Tests/WheelSettingsTests.cs`：既定値・JSON・設定範囲を検証。
   - `LogiGap.Tests/WheelTraceTests.cs`：分類・書式・カルチャ非依存を検証。
   - `README.md`：設定、ログ、実機確認手順を追記。

2. ビルドとテストの結果  
   指定の両コマンドが成功しました。ビルドは**警告0・エラー0**。テストは**249件すべて合格、失敗0・スキップ0**です。

3. 指示書から外れた点  
   無し。`app/publish/`・`logs/` は変更せず、実アプリ・HID・フック・入力送信・通知は起動していません。

4. 新しい `// 要確認:`  
   - `smoothMs=0` で変換器を直接使う場合は分割数を1にする。
   - worker終了待ちのタイムアウト時はタイマー解放を延期する。
   - 記録の再有効化でも起動全体の200,000件上限を維持する。
   - 書き込み失敗時は記録を停止し、false→trueで再試行する。

5. 最初の実機確認  
   記録を有効にし、hook・50%・40/8で両方向の初動と滑らかさ、Options+への引き継ぎ、一時停止時の残量破棄を確認してください。ログの小分け送信量・間隔を比較し、測定後は記録をfalseに戻してください。