1. **変えたファイルと内容**

   すべて `app/` 内です。

   | ファイル | 内容 |
   |---|---|
   | `PartsTray.Core/WindowSwitchPolicy.cs` | 候補・切り替え先・復元・下へ回す操作を純粋な関数に分離 |
   | `PartsTray/MaximizedWindowSwitcher.cs` | 最小化した窓、カーソル側モニター、200ms 後の確認・再試行・古い確認の取り消しに対応 |
   | `PartsTray/GestureModule.cs` | 切り替え処理とタイマーの寿命を管理 |
   | `PartsTray/GestureModule.Workers.cs` | 右ホイールの切り替えを UI スレッドへ渡す |
   | `PartsTray/WindowKeysModule.cs` | 終了時に確認タイマーを破棄 |
   | `PartsTray.Tests/WindowSwitchPolicyTests.cs` | 選択ロジックのテストを 34 件追加 |
   | `README.md` | 新しい動作と 11/21 の実機状況を説明 |

2. **テストとビルド結果**

   **819 件すべて合格、失敗 0、スキップ 0。ビルド成功、警告 0・エラー 0。**

   指定コマンドは SDK 探索先へのアクセス拒否が出たため、§0 で許可された一時引数 `TargetPlatformSdkPath`・`TargetPlatformDisplayName` を付け、ビルド・テストとも正常終了を確認しました。

3. **判断したこと・迷ったこと**

   候補が 1 つの場合は §2 の詳細を優先し、最小化中で「今の窓」がない場合のみ復元します。開いている候補が 1 つなら何もしません。復元位置は画面座標へ変換して判定します。[Windows の座標仕様](https://learn.microsoft.com/ja-jp/windows/win32/api/winuser/ns-winuser-windowplacement)

   既存の未コミット変更・改行コード・既定設定を保持しました。§0 の禁止対象の変更、アプリや機器操作プログラムの起動、ダウンロード、git による書き換えは行っていません。

4. **実機で確かめてほしいこと**

   - 最大化から最小化した窓が、キー・右ホイールで最大化に戻ること。
   - 前面とは別のモニターにカーソルを置いても、初回から切り替わること。
   - 高速な連続操作で古い窓へ戻らず、前面化成功時に失敗ログが出ないこと。
   - キー未設定・右ホイール無効時の動作が従来どおりであること。