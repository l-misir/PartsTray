`codex/指示書M_設定ウィンドウ.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap.Core/Settings.cs`・`app/LogiGap.Core/GestureSettings.cs`（設定の形、既定値、`Validate`、`Load` / `Save`）
  - `app/LogiGap/Program.cs`（`TrayContext`：メニュー、左クリック、`show` の合図、`Reload`、自動起動のメニュー）
  - `app/LogiGap.Core/GapSession.cs`（`ButtonOrder` と、`buttons` にキーが無いボタンの扱い）
  - `app/LogiGap/BatteryModule.cs`（`devices`）
  - リポジトリ直下の `README.md`（ボタンの名前の表、かざぐるマウスの機能の説明）と `app/README.md`（各設定の意味）
  - `app/LogiGap.Tests/` の設定のテスト
- 作業の順番：本体（指示書の §2〜§5）を先に書いてビルドを通し、その後でテスト（§6）、最後に README（§7）
- この実行は無人です。質問はできません。判断が必要な点は、指示書の方針に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください（`codex/` の指示書とリポジトリ直下の README も変えない）。`app/publish/` と `logs/` には触れないでください。アプリ・HID・フック・入力の送信・通知・トレイのアイコン・ウィンドウを実際に動かすプログラムは起動しないでください。インターネットから何もダウンロードしないでください
- このフォルダは Git で管理されています。コミット・ブランチの作成・`git` での書き換え（checkout・reset・stash など）はしないでください（`git diff` / `git status` を読むのはかまいません）
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書M §9 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
