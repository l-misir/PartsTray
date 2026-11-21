画面端の高速スクロール判定に `diagnostics.scrollProbe` を追加しました。既定はオフです。指定コマンドでビルドは警告 0・エラー 0、テストは **全 1,136 件合格**（失敗 0・スキップ 0）でした。§0 に従い、検証はビルドと自動テストで行いました。

### 直した原因の見込みと判断

- **エクスプローラー**では、従来の 50 ms を超えた UI オートメーション（UIA）の結果が捨てられ、次の判定でも同じ待ち直しが起き得ました。待機を元の点・内側の点を合わせて **300 ms** に変更しました。時間切れの回は通常速度で通し、あとから得た結果は対象の窓を再確認して、完了から 2 秒間再利用します。
- **ホイール側の `Fresh`** は、元のカーソル座標を正しく使っていました。一方、100 ms の有効期限は依頼時刻から数えていたため、待機を延ばすだけでは遅い成功結果が届いた時点で失効します。期限を判定完了時から数えるようにし、240 ms かかった内側の判定が元の座標で効くことをテストしました。
- **UIA の親探索**は 8 段から 32 段に広げました。縦スクロール可能な親の右端に加え、UIA が縦向きの `ScrollBar` を公開する場合はその部品の矩形内でも判定します。**VS Code** の Chromium 判定は既に内側の座標を使っており、最外周の 1～2 ピクセルだけが枠になるケースを回帰テストで確認しました。

### 診断の読み方と、Claude が実機で確かめること

「ウィンドウ・ホイール」で高速スクロールと端の補助をオンにし、「その他」→「高速スクロールの判定を記録する」→「適用」を選びます。当日の `logs/app_yyyyMMdd.log` で `ScrollProbe` を検索してください。窓はクラス名だけを記録し、同じ位置・結果の連続分は約 1 秒ごとにまとめます。

`edgeLRTB` の **2 番目が 0** なら画面の右端です。`usedInside=yes` は内側も調べたこと、`result` の `@inside` はそこで一致したことを示します。`uiaWaitMs` は今回の待ち時間、`queryMs` と `cache` は元の点／内側の点の順です。

| アプリ | 実機で見るところ |
|---|---|
| エクスプローラー | 長い一覧を最大化し、同じ高さで本文・バー・右端を順に回します。`DirectUIHWND` の行で `patternDepth` が 9 以上なら従来の探索範囲外です。`Timeout` のあと、同じ位置で `cache` が `yes` になり `result=UIA@inside` が出れば、遅れて得た結果が効いています。 |
| VS Code | 長いファイルの右端で `class` と `insideClass` を比べます。元が枠、内側が `Chrome_RenderWidgetHostHWND` で `result=Chromium@inside` なら、内側の帯で成功です。`usedInside=no` なら `edge`、試して外れたら `native` の内側の理由を確認します。 |
| Acrobat | 複数タブを開いて届け直しをオンにし、最大化した窓の上端で回します。初回の「画面の端のホイールを内側の子ウィンドウに届け直しました」と、実際のタブ切り替えを照合します。 |

### 変更ファイル

| 場所 | ファイル |
|---|---|
| `app/PartsTray.Core/` | `Settings.cs`、`PointerWindowPolicy.cs`、`ScrollBarPolicy.cs`、`ScrollBarProbe.cs`、`SingleFlightWorker.cs`、`ScrollProbeLog.cs` |
| `app/PartsTray/` | `EdgeWheelWindows.cs`、`ScrollBarAutomation.cs`、`ScrollProbeDetails.cs`、`WindowsGestureServices.cs`、`WindowsGestureServices.ScrollProbe.cs`、`GestureModule.cs`、`GestureModule.Workers.cs`、`SettingsWindow.Icons.cs` |
| `app/PartsTray.Tests/` | `EdgeWheelGestureTests.cs`、`PointerWindowTests.cs`、`ScrollBarPolicyTests.cs`、`ScrollProbeWaitTests.cs`、`ScrollProbeLogTests.cs`、`WheelSettingsTests.cs` |
| 説明 | [app/README.md の診断手順](<../../app/README.md:64>) |