1. **変更したファイル**
   - [GapSession.cs](../../app/LogiGap.Core/GapSession.cs)：サムホイールはパラメーター6バイト以上・回転量非ゼロの場合だけ記録。
   - [LogiGap.Tests.csproj](../../app/LogiGap.Tests/LogiGap.Tests.csproj)：固定コピーを取り込む設定に変更。
   - [ReplayTests.cs](../../app/LogiGap.Tests/ReplayTests.cs)：最終検証をキー・ホイール入力ゼロに戻し、不要な分岐・コメントを削除。
   - [SwitchObservationTests.cs](../../app/LogiGap.Tests/SwitchObservationTests.cs)：回転0・短い通知・正負の回転を検証する4件を追加。
   - [README.md](../../app/README.md)：今回の仕様・検証結果を反映。
   - `LogiGap.Tests/Fixtures/` に固定入力を追加：`230321.log` は指定境界直前までの**1166行**、`224803.log` は全**156行**。UTF-8（BOMなし）・CRLFを維持し、元ログとのバイト一致も確認。

2. **ビルド・テスト結果**  
   指定の `--no-restore` コマンドは両方成功。ビルドは警告0・エラー0、テストは**172件すべて合格、失敗0・スキップ0**です。

3. **指示書・レビューから外れた点**  
   無し。`logs/` は読み取りのみ。アプリや実機操作は起動していません。

4. **新しい `// 要確認:`**  
   無し。レビューで削除を指定されたコメントは削除しました。

5. **実機で最初に確かめること**  
   引き継ぎ後、サムホイールに触れるだけでは回転検出ログが出ず、実際に回すとそのリンクで1行だけ出ることを確認してください。