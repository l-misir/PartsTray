1. 変更したファイルと内容  
   - [GapSession.cs](../../app/LogiGap.Core/GapSession.cs)：hook の適用末尾で状態を照会し、残った横取りだけを `(0, 0)` で解除。許可条件を追加。
   - [NativeWheelTests.cs](../../app/LogiGap.Tests/NativeWheelTests.cs)：指定の既存テスト3件を修正。
   - [RemainingThumbDiversionTests.cs](../../app/LogiGap.Tests/RemainingThumbDiversionTests.cs)：引き継ぎとの競合・後片付けなど10ケースを追加。
   - [README.md](../../app/README.md)：指示書Gの節を追加し、hook の説明を更新。

2. ビルドとテストの結果  
   指定の両コマンドが成功しました。ビルドは警告0・エラー0。テストは **271件すべて合格、失敗0・スキップ0**。

3. 指示書から外れた点と理由  
   無し。実機操作を行うプログラムは起動していません。

4. 新しい `// 要確認:`  
   [GapSession.cs:572](../../app/LogiGap.Core/GapSession.cs:572) の1件：2バイト未満の応答は横取りなしとして扱い、解除を書き込まない。

5. 実機で最初に確かめること  
   横取りが残った状態で hook の肩代わりを開始し、解除の送信・OK応答・「適用完了」と横スクロールの復帰を確認してください。続けて Options+ への引き継ぎ時に停止・カクつきがないことを確認してください。