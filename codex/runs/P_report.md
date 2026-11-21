実装を反映しました。**全 785 テストは合格しましたが、指定された 2 コマンドの正常終了は、SDK 探索先へのアクセス拒否により未達です。**

**1. 変えたファイルと内容**

すべて `app/` 内です。

| ファイル | 内容 |
|---|---|
| `PartsTray.Core/GestureSettings.cs` | `RightWheelSwitch` を既定 false で追加 |
| `PartsTray.Core/WindowSwitchWheel.cs` | 共通計算に、右ボタン解放まで続く方式を追加 |
| `PartsTray.Core/GestureEngine.cs` | ホイール判定と押下終了処理を接続 |
| `PartsTray.Core/GestureEngine.RightWheel.cs` | 入力取り込み、クリック・ジェスチャー抑止、集計を追加 |
| `PartsTray.Core/GestureEngine.Timing.cs` | 時間管理を分離し、右ホイールの長押しに対応 |
| `PartsTray.Core/GestureInput.cs` | 既存の型・軌跡認識を分離し、切り替え効果を追加 |
| `PartsTray/MaximizedWindowSwitcher.cs` | 候補収集・前後切り替え・前面化再試行を共通化 |
| `PartsTray/WindowKeysModule.cs` | 共通の切り替え処理を利用 |
| `PartsTray/WindowKeysModule.Wheel.cs` | キー後のホイール操作も共通処理へ接続 |
| `PartsTray/GestureModule.cs` | 切り替え効果をワーカーへ振り分け |
| `PartsTray/GestureModule.Workers.cs` | フック外での切り替え・記録を実装 |
| `PartsTray/SettingsWindow.Gesture.cs` | チェック項目と説明を追加 |
| `PartsTray.Tests/RightWheelSwitchTests.cs` | 方向・蓄積・抑止・一時停止などを検証 |
| `PartsTray.Tests/RightWheelSettingsTests.cs` | 既定値・旧 JSON・保存形式を検証 |
| `README.md` | 右ボタン＋ホイールの節を追加 |
| `改造の手引き.md` | 編集先の表を更新 |

**2. テスト・ビルド結果**

- テスト：**785 件合格、失敗 0、スキップ 0**。
- 指定ビルド：警告 0、エラー 1。`AppData\Local\Microsoft SDKs` へのアクセス拒否（MSB4184）。
- 指定テスト：全テスト合格。ただし同じ SDK エラーでコマンドの終了コードは 1。
- SDK 探索用の値を一時的なコマンド引数で指定した追加検証：**ビルド成功、警告 0・エラー 0。テストも正常終了**。

**3. 判断したこと**

方向・蓄積量と Windows の切り替え処理は、それぞれ一か所に共有しました。しきい値未満でもホイールを取り込み、解放時のクリック・命令を抑止します。

有効時は長押し後も使えるよう解放まで押下を保持し、右ドラッグへの影響を画面と README に記載しました。既定のオフでは従来動作を維持します。

既存の未コミット変更と改行コードを保持しました。`csproj`・環境変数・パッケージは変更せず、アプリや機器操作プログラムの起動、ダウンロード、git による書き換えも行っていません。

**4. 実機で確かめてほしいこと**

- 複数の最大化ウィンドウで、縦・横の前後切り替えと横方向反転。
- 少量だけ回して離した場合も、右クリックメニューやジェスチャー命令が出ないこと。
- 軌跡・吹き出しの消去、押下ごとの集計ログ。
- オフ時・一時停止時の従来動作と、既存のキー後ホイール方式との併用。
- SDK 探索先を読める環境で、指定のビルド・テストコマンドの正常終了。