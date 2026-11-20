1. **変更ファイル**
   - [Settings.cs](../../app/LogiGap.Core/Settings.cs)：CPU設定・`split`追加、オウムの既定を`none`へ変更。
   - [Monitor.cs](../../app/LogiGap.Core/Monitor.cs)：最大文字サイズを選ぶ純粋関数を追加。
   - [DigitArt.cs](../../app/LogiGap/DigitArt.cs)：大きな数字、サイズのキャッシュ、3桁の縮小処理を追加。
   - [MonitorModule.cs](../../app/LogiGap/MonitorModule.cs)：4アイコン化、中央配置、差し替え抑制、ドラッグ中の更新保留、ハンドル解放を実装。
   - [TrayIds.cs](../../app/LogiGap/TrayIds.cs)：番号定数とリフレクションによる確認を追加。
   - [BatteryModule.cs](../../app/LogiGap/BatteryModule.cs)：コンストラクタの番号確保処理を変更。描画は維持。
   - [MonitorSettingsTests.cs](../../app/LogiGap.Tests/MonitorSettingsTests.cs)・[MonitorFitTests.cs](../../app/LogiGap.Tests/MonitorFitTests.cs)：既定値更新と19件のテスト追加。
   - [README.md](../../app/README.md:214)：新構成・設定例・表示手順・確認項目を更新。

2. **番号と起動経路**
   
   生成順は、本体 **1** → オウム **2** → 非表示で生成・破棄 **3〜8** → CPU **9** → メモリ **10** → 電池 **11〜16**です。  
   コード確認では、`GapModule`・`GestureModule`・`HiddenWindowManager`の起動時にNotifyIconを追加生成する経路はありません。実行時の番号確認は未実施です。

3. **数字の実測**
   
   20px・thinの実描画寸法は **未確認**です。指示書の期待値は`basePx = 20.5`、「88」が幅20・高さ15です。サイズ選択の計算はテスト済みです。

4. **`// 要確認:`**
   
   今回追加・移動した箇所は4つです。
   - `TrayIds.cs:21`：WinFormsの内部番号・`_id`への依存。
   - `TrayIds.cs:22`：電池側の変更範囲を限定するため、既存枠の`target`を反射で取得。
   - `MonitorModule.cs:63`：変更対象外の`Program.cs`に代わり、本体rankを2で再登録。
   - `MonitorModule.cs:99`：ドラッグを妨げないよう設定反映と解放を延期。
   
   既存の初回CPU値、欠測表示、共有メモリ、テーマ、不正データ、電池識別に関するコメントは維持しています。

5. **検証結果**
   
   `dotnet build app/LogiGap.sln --no-restore`：**成功、警告0・エラー0**。  
   `dotnet test app/LogiGap.sln --no-restore`：**474件すべて合格、失敗0・スキップ0**（既存455件＋追加19件）。

6. **実機で確認してほしいこと**
   
   新しいCPU・メモリアイコンをWindows設定で表示し、希望順に並べてください。125%での数字の大きさ・中央配置、2段表示への復帰、ドラッグ、メニュー、無効化・終了時の解放、電池の表示維持を確認してください。
   
   アプリや実機操作プログラムは起動していません。ダウンロード、パッケージ追加、環境変数変更、`app/`外・`app/publish/`・`logs/`への変更は行っていません。