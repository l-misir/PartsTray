完了しました。6ボタン対応、任意キー入力、Hold、引き継ぎ・後片付けまで実装しました。

**変えたファイル：すべて `app/` 内の19件**

- Core：`GapSession.cs`、分割した `GapSession.{Commands,Discovery,Notifications,Worker}.cs`、`Settings.cs`、`SettingsText.cs`
- 画面：`SettingsWindow.{GapButtons,Keys,Mouse}.cs`、`WindowKeysModule.HoldButton.cs`
- テスト：`GapButtonActionsTests.cs`、`GapButtonSettingsTests.cs`、`ReplayTests.cs`、`ReviewTests.cs`、`SessionTests.cs`、`SettingsTextTests.cs`
- 説明：[README.md](../../app/README.md)、[改造の手引き.md](../../app/改造の手引き.md)

**テスト件数：990件すべて合格**（既存953件＋追加37件、失敗・スキップ0）。全体ビルドも警告・エラー0です。

**判断したこと：**既定値と旧設定の互換性を維持しました。0x00C4は設定した場合だけ横取りします。Holdは入力を送らず、押下・解放を通知します。変更したC#ファイルはすべて300行以内で、既存の改行コードも維持しました。

**実機で確かめてほしいこと：**6ボタンの表示、任意キーの保存・再表示、Holdでのホイール切り替え、0x00C4の未設定時／設定時の動作、Options+への引き継ぎ前後の二重実行と横取り残りの有無です。

§0に従い、禁止された場所・csproj・manifestは変更せず、アプリ起動・機器操作・ダウンロード・gitによる書き換えは行っていません。