`codex/指示書G_残った横取りを解除する.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap.Core/GapSession.cs`（特に `NextCommand` の applyStep 6・7、`ValidateCommand`、`Match`、`Read` / `Write`、`PrepareCleanup`、`ObserveOptions`）
  - `app/LogiGap.Tests/Fakes.cs`（`Harness` の `Reply`、`Thumb`、`Intercept`、`Writes`）
  - `app/LogiGap.Tests/NativeWheelTests.cs`、`SessionTests.cs`、`OptionsSwidTests.cs`、`ReplayTests.cs`
  - 参考：`codex/指示書D_サムホイールのフック方式.md`
- この実行は無人です。質問はできません。判断が必要な点は、指示書の「理由」に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください。`app/publish/` と `logs/` には触れないでください。アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しないでください
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書G §6 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
