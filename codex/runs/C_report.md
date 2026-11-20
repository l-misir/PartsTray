1. **変更したファイル**
   - [GapSession.cs](../../app/LogiGap.Core/GapSession.cs)：非実行入力・破棄操作のログ、切り替え通知を追加。
   - [Settings.cs](../../app/LogiGap.Core/Settings.cs)：`NotifyOnSwitch` を追加。既定値は `true`。
   - [Program.cs](../../app/LogiGap/Program.cs)：UIスレッドでの通知表示と購読解除を追加。
   - [SwitchObservationTests.cs](../../app/LogiGap.Tests/SwitchObservationTests.cs)：26件のテストを追加。
   - [ReplayTests.cs](../../app/LogiGap.Tests/ReplayTests.cs)：実ログに合わせて検証を修正。
   - [README.md](../../app/README.md)：設定、ログの意味、実機確認手順を追記。

2. **ビルド・テスト結果**  
   `dotnet build app/LogiGap.sln --no-restore`：成功、警告0・エラー0。  
   `dotnet test app/LogiGap.sln --no-restore`：**168件すべて合格、失敗0・スキップ0**。  
   アプリ・実機操作は起動していません。

3. **指示書から外れた点と理由**  
   既存の再生テストを修正しました。今回の観察・通知処理を無効にしても、ログ後半のActive中の入力で失敗したためです。「全期間の入力ゼロ」を、非Active時の入力増分ゼロとActive中の具体的な入力件数の検証に変更しました。アプリの入力動作は変更していません。

4. **新しい `// 要確認:` 一覧**
   - 不完全なボタン通知は無視し、押下履歴を保持する。
   - 通知の秒数は表示言語によらず小数点・小数2桁に固定する。
   - 実ログ再生では、全期間ゼロではなく引き継ぎ後の入力増分ゼロを検証する。

5. **実機で最初に確かめること**  
   引き継ぎ通知の直後にメモ帳で貼り付けを試し、「LogiGap は実行しない」のログが押下ごとに1行出て、貼り付けも1回だけ起きることを確認してください。