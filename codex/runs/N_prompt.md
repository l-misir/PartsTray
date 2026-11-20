`codex/指示書N_誰でもそのまま使えるようにする.md` を最後まで読み、そのとおりに実装してください。

- 対象のコードは `app/` の下にあります。まず次のファイルを読んで、既存の作りを把握してから変更してください
  - `app/LogiGap/BatteryModule.cs`（電池の枠・描き方・Bluetooth の読み取り）、`app/LogiGap/MonitorModule.cs`・`DigitArt.cs`（数字とオウム）、`app/LogiGap/TrayIds.cs`
  - `app/LogiGap/GapModule.cs`・`app/LogiGap.Core/GapSession.cs`（肩代わりの有効・無効、受信機の開き方、WPID の扱い）、`app/LogiGap.Core/Battery.cs`
  - `app/LogiGap.Core/Settings.cs`、`app/LogiGap/SettingsWindow*.cs`、`app/LogiGap/Program.cs`
  - `app/README.md`（指示書 J・K・L・M の節）と `app/LogiGap.Tests/`
- 作業の順番：§2（ファイルの分け方。ここでビルドとテストを通す）→ §3 → §4 → §5 → テスト（§6）→ README（§7）
- この実行は無人です。質問はできません。判断が必要な点は、指示書の方針に沿って決め、決めた箇所に `// 要確認:` を残してください
- `app/` の外は変えないでください（`codex/` の指示書とリポジトリ直下のファイルも変えない）。`app/publish/`・`logs/`・`app/LogiGap/Assets/` には触れないでください。アプリ・HID・フック・入力の送信・通知・トレイのアイコン・ウィンドウを実際に動かすプログラムは起動しないでください（機器の情報を読むだけのコマンドはかまいません）。インターネットから何もダウンロードしないでください
- このフォルダは Git で管理されています。コミット・ブランチの作成・`git` での書き換え（checkout・reset・stash など）はしないでください（`git diff` / `git status` を読むのはかまいません）
- パッケージは追加せず、環境変数も変えないでください。復元は済んでいます
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 最後のメッセージは、指示書N §9 の形式の完了報告（日本語、テスト件数と合否を含める）にしてください
