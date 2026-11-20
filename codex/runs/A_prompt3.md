前回の作業は、利用上限に達して途中で止まりました。止まった時点では、`dotnet build app/LogiGap.sln --no-restore` が `app/LogiGap.Tests/Fakes.cs(43,41)` の CS0411（GetValueOrDefault の型推論）で失敗していました。

続きから、`codex/runs/A_review1.md` の修正 1〜4 をすべて完了してください。

- まず、ここまでの変更を読み直してください（GapSession.cs, Fakes.cs, ReplayTests.cs, SessionTests.cs, ReviewTests.cs）。修正 1〜4 のうち、どこまで済んでいるかを確かめてから進めてください
- 修正 4 の片付け（一時ファイルの削除と `app/README.md` の更新）も忘れないでください
- `dotnet build app/LogiGap.sln --no-restore` と `dotnet test app/LogiGap.sln --no-restore` が両方成功し、全テストが合格するまで修正を繰り返してください
- 指示書A の §0 と、前回までの依頼文の制約（`app/` の外を変えない、実機とアプリを起動しない、パッケージを追加しない、環境変数を変えない）は引き続き有効です
- 最後のメッセージは、今回の修正分の完了報告（日本語、指示書 §12 の形式、テスト件数と合否を含める）にしてください
