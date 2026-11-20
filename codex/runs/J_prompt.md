`codex/指示書J_オウムと温度メモリのアイコン.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap/Program.cs`（`TrayContext`：`icon`、`menu`、`StateChanged`、`Reload`、`Dispose`、`modules`）
  - `app/LogiGap/GapModule.cs`（`IFeatureModule` の形）、`app/LogiGap/GestureModule.cs`（`SettingsChanged`・`Dispose` の書き方の参考）
  - `app/LogiGap.Core/Settings.cs`、`app/LogiGap/LogiGap.csproj`
  - `app/LogiGap.Tests/WheelSettingsTests.cs`（設定のテストの書き方の参考）
- 作業の順番：本体のコード（指示書の §2〜§6）を先に書いてビルドを通し、その後でテスト（§7）、最後に README（§8）
- この実行は無人です。質問はできません。判断が必要な点は、指示書の方針に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください。`app/publish/` と `logs/` には触れないでください。アプリ・HID・フック・入力の送信・通知・トレイのアイコンを実際に動かすプログラムは起動しないでください。インターネットから何もダウンロードしないでください
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書J §10 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
