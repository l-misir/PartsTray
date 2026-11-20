1. **変更ファイルと内容**

   | ファイル（`app/` 内） | 変更内容 |
   |---|---|
   | `LogiGap.Core/Settings.cs` | 電池設定・入力検証を追加 |
   | `LogiGap.Core/Protocol.cs` | swid指定に対応。既定値5を維持 |
   | `LogiGap.Core/Battery.cs` | 機器探索、残量解析、再送、定期照会、切断時の値保持 |
   | `LogiGap.Core/Monitor.cs` | 機器のドット絵を追加 |
   | `LogiGap/GapModule.cs` | 受信分配、送信排他、電池専用タイマー |
   | `LogiGap/HidConnection.cs` | 長い形式のswid 7を許可 |
   | `LogiGap/BatteryModule.cs` | Bluetooth読取、機器別アイコン、テーマ対応・破棄処理 |
   | `LogiGap/Program.cs` | 電池モジュールを登録 |
   | `LogiGap.Tests/BatteryTests.cs` | §7と追加の境界条件を検証 |
   | `README.md` | 指示書Jの後にKの説明・設定・確認手順を追加 |

2. **ビルド・テスト結果**

   - `dotnet build app/LogiGap.sln --no-restore`：成功。警告0、エラー0。
   - `dotnet test app/LogiGap.sln --no-restore`：**437件すべて合格**。失敗0、スキップ0。

3. **指示書から外れた点と理由**

   許可ファイル一覧にない `HidConnection.cs` を最小限変更しました。既存の検査がswid 5以外を拒否しており、指定されたswid 7の送信に必要だったためです。

   `app/` 外、`app/publish/`、`logs/` は変更していません。実機動作を伴うプログラムの起動、ダウンロード、パッケージ追加、環境変数変更も行っていません。

4. **新しい `// 要確認:`：9件**

   - 複数レベルbit時は低い残量を採用。
   - 別レシーバーでは機能番号を再探索。
   - 再ペアリングした番号は別機器扱い。
   - 重複した機能番号は不正として扱う。
   - 機器種類は0＝キーボード、3＝マウス、その他を区別。
   - テーマ取得失敗時は最後の色を維持。
   - Bluetoothの名前取得失敗時はPnPインスタンスIDを表示。
   - Bluetooth切断時の `DN_STARTED` の挙動。
   - `HidConnection.cs` のswid 7許可への拡張。

5. **最初の実機確認**

   3台のアイコン・名前・残量を確認してください。続いて充電時の緑表示、切断時の薄い表示、再接続、`hidden`、右クリックメニュー、Options+への引き継ぎを確認してください。特にWH-1000XM6の切断判定は未確認です。Windows 11の「隠れたアイコン」も確認してください。