`codex/指示書E_横スクロールの記録と滑らか化.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap.Core/GapSession.cs`（特に `TryTakeHorizontal`、`Change`、`StopBridge`、`SetPaused`、`UpdateSettings`、`Pump` の先頭のキュー処理、`WorkerWaitMilliseconds`）
  - `app/LogiGap.Core/Settings.cs`、`app/LogiGap.Core/Protocol.cs`（`NativeWheelScaler`）、`app/LogiGap.Core/InputSequence.cs`（`KeyStroke.ExtraInfo`）
  - `app/LogiGap/GestureModule.cs`（`Hook`、`SettingsChanged`、`Dispose`）、`app/LogiGap/GapModule.cs`（worker のループ）、`app/LogiGap/FileLog.cs`（ファイル書き込みの作り方の参考）、`app/LogiGap/Program.cs`
  - `app/LogiGap.Tests/Fakes.cs`、`NativeWheelTests.cs`、`ProtocolTests.cs`
  - 参考：`codex/指示書D_サムホイールのフック方式.md`（橋渡しの作り）
- この実行は無人です。質問はできません。判断が必要な点は、指示書の「理由」に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください。`app/publish/` と `logs/` には触れないでください。アプリ・HID・フック・入力の送信・通知を実際に動かすプログラムは起動しないでください
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書E §10 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
