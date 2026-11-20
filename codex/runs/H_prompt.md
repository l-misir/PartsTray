`codex/指示書H_Options+の再起動を見分ける.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap.Core/GapSession.cs`（特にコンストラクタ、`NextCommand` の接続判定、`ObserveOptions`、`Begin`、`inheritedActive` と `previousActive`）
  - `app/LogiGap/GapModule.cs`（`OptionsRunning` と `GapSession` を作る箇所）
  - `app/LogiGap.Tests/Fakes.cs`（`Harness`）、`ReviewTests.cs`、`SessionTests.cs`、`RemainingThumbDiversionTests.cs`、`OptionsSwidTests.cs`
- この実行は無人です。質問はできません。判断が必要な点は、指示書の「理由」に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください。`app/publish/` と `logs/` には触れないでください。アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しないでください
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書H §7 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
