1. 変更したファイルと内容

   - `Settings.cs`：`Divert` を廃止し、`hook / divert / off` の設定・検証を追加。
   - `Protocol.cs`：橋渡しインターフェースと端数を保持する変換器を追加。
   - `GapSession.cs`：橋渡しの開始・終了、キュー送信、期限、失敗ログ、読み戻し判定を実装。
   - `GestureModule.cs`：横スクロールを橋渡しへ接続し、注入された入力を除外。
   - `Program.cs`：`gap.Session` をフックへ渡すよう変更。
   - `Fakes.cs`：ホイール送信失敗の切り替えを追加。
   - `SessionTests.cs`、`ReviewTests.cs`、`SwitchObservationTests.cs`、`ReplayTests.cs`：横取り前提のテストで `divert` を明示。既存の期待値は維持。
   - `NativeWheelTests.cs`（新規）、`ProtocolTests.cs`：橋渡し・変換などの検証を追加。
   - [README.md](../../app/README.md)：設定、制限、ログ、実機確認手順を更新。

2. ビルドとテストの結果

   `dotnet build app/LogiGap.sln --no-restore`：成功、警告 0・エラー 0。  
   `dotnet test app/LogiGap.sln --no-restore`：**全 209 件合格、失敗 0・スキップ 0**（追加 37 件）。

3. 指示書から外れた点と理由

   無し。アプリ・実機操作は未実行です。`app/publish/`、`logs/`、環境変数、パッケージは変更していません。

4. 新しく残した `// 要確認:` の一覧

   - [Protocol.cs:70](../../app/LogiGap.Core/Protocol.cs:70)：反転符号が Options+ と同じ向きになるか。
   - [GapSession.cs:284](../../app/LogiGap.Core/GapSession.cs:284)：終了後の残りキューでも失敗ログの重複抑制を維持。
   - [GapSession.cs:416](../../app/LogiGap.Core/GapSession.cs:416)：送信件数・例は成功した非ゼロ送信を集計。

5. 実機で最初に確かめること

   `hook`・速度 50%・方向 1 で両方向へ回し、Options+ への引き継ぎ前後で方向・速度が揃い、停止やカクつきがなく、クリックせず使い続けられることを確認してください。橋渡し終了ログの理由と、二重スクロール・再送ループがないことも確認対象です。