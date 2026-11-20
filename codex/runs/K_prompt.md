`codex/指示書K_周辺機器ごとの電池アイコン.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap/GapModule.cs`（`Transport`、`Monitor` の受信の分配、`ReceiverOpened` / `ReceiverLost` の呼び方）
  - `app/LogiGap.Core/Protocol.cs`（`HidReport`、`Request`）、`app/LogiGap.Core/GapSession.cs`（`Receive`、`ObserveOptions`、`Match` の swid の扱い）
  - `app/LogiGap/MonitorModule.cs` と `app/LogiGap.Core/Monitor.cs`（指示書J：アイコンの描き方、`IconSlot`、数字のフォント、テーマの色）
  - `app/LogiGap/Program.cs`（`TrayContext`、`modules`、`menu`）、`app/LogiGap.Core/Settings.cs`
  - `app/LogiGap.Tests/Fakes.cs`（`Harness`）、`OptionsSwidTests.cs`
- 作業の順番：本体のコード（指示書の §2〜§6）を先に書いてビルドを通し、その後でテスト（§7）、最後に README（§8）
- この実行は無人です。質問はできません。判断が必要な点は、指示書の方針に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください。`app/publish/` と `logs/` には触れないでください。アプリ・HID・フック・入力の送信・通知・トレイのアイコンを実際に動かすプログラムは起動しないでください。インターネットから何もダウンロードしないでください
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書K §10 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
