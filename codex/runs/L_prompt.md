`codex/指示書L_CPUと温度とメモリを分ける.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap/MonitorModule.cs`（`DrawParrot`・`DrawTempMemory`・`DrawText`・`UpdateTooltip`・`IconSlot`・`SettingsChanged`・`Dispose`）
  - `app/LogiGap/DigitArt.cs`（"thin" の数字の作り）、`app/LogiGap.Core/Monitor.cs`（`MonitorFont`・`MonitorMath`）
  - `app/LogiGap.Core/Settings.cs`（`MonitorSettings`・`ParrotSettings`・`TempMemorySettings`・`Validate`）
  - `app/LogiGap/Program.cs`（`TrayContext` の NotifyIcon とモジュールを作る順番）、`app/LogiGap/BatteryModule.cs`（コンストラクタの番号のずらし方）、`app/LogiGap/TrayOrder.cs`、`app/LogiGap/TrayInteraction.cs`
  - `app/LogiGap/GapModule.cs`・`app/LogiGap/GestureModule.cs`・`app/LogiGap/HiddenWindowManager.cs`（起動時に NotifyIcon を作っていないかの確認だけ）
  - `app/LogiGap.Tests/` の設定とモニターのテスト
- 作業の順番：本体のコード（指示書の §2〜§5）を先に書いてビルドを通し、その後でテスト（§6）、最後に README（§7）
- この実行は無人です。質問はできません。判断が必要な点は、指示書の方針に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください（`codex/` の指示書も変えない）。`app/publish/` と `logs/` には触れないでください。アプリ・HID・フック・入力の送信・通知・トレイのアイコンを実際に動かすプログラムは起動しないでください。インターネットから何もダウンロードしないでください
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書L §9 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
