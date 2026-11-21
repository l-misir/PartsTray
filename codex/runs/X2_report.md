Chromium の描画子窓の右端で、高速スクロールを判定する処理を追加しました。**ビルドは警告 0・エラー 0、テストは追加 13 件を含む全 1,048 件が合格**しました。失敗・スキップは 0 件です。アプリは起動しておらず、実機での動作は未確認です。

### 判断したこと

Windows 標準の縦スクロールバーを最初に調べます。そこで該当せず、カーソル直下の窓が `Chrome_RenderWidgetHostHWND` なら、その子窓のクライアント領域の右端から内側へ、カーソルがある画面の `SM_CXVSCROLL` 幅＋2 物理ピクセルを対象にします。

帯に入れば UI オートメーションを待たずに判定します。位置で決めるため、バーが自動で隠れている間も帯は対象です。帯の外では従来の UI オートメーションを使い、ページ内の独立したスクロール枠も引き続き調べます。

### 変更したファイル

- 判定順・右端の帯・初回ログと、そのテスト — [PointerWindowPolicy.cs](<../../app/PartsTray.Core/PointerWindowPolicy.cs>)、[PointerWindowTests.cs](<../../app/PartsTray.Tests/PointerWindowTests.cs>)
- 子窓のクライアント領域を画面座標で取得し、画面ごとのスクロールバー幅を両方の判定で共有 — [GestureNative.cs](<../../app/PartsTray/GestureNative.cs>)、[WindowsGestureServices.cs](<../../app/PartsTray/WindowsGestureServices.cs>)、[ScrollBarAutomation.cs](<../../app/PartsTray/ScrollBarAutomation.cs>)
- 既存の設定項目の説明とホイール節 — [SettingsWindow.Mouse.cs](<../../app/PartsTray/SettingsWindow.Mouse.cs>)、[app/README.md](<../../app/README.md:46>)

### Claude が実機で確かめること

1. **Chrome・Edge** で長いページを開き、ページ右端では設定の 50 倍、本文中央では通常速度になること。開発者ツールを右に固定すると、ページとツールの境界に移ったページ右端でも確かめられます。
2. **Claude デスクトップ**の長い会話でも、描画領域の右端と本文中央で同じ違いが出ること。表示倍率の異なるモニターがあれば、窓を移して再確認してください。
3. 同じ起動中に Chrome・Edge・Claude の右端を繰り返し使っても、ログの「スクロールバー（Chromium の右端）を見分けました」が **1 回だけ**であること。**タスクマネージャー**では、前回と同じ縦スクロールバーで引き続き高速になること。