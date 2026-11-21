`monitor.digitStyle` が `thin` のとき、`monitor.digitFont` を `Terminus`（Regular）と `TerminusBold` に切り替えて比較できます。両書体は PartsTray に埋め込み、PC へのインストールは不要です。既定の `Tahoma` と、Windows のフォント名による指定も維持しました。

0〜150 が字間 1 ドットで収まる最大の原寸を使い、ぼかしや拡大縮小を加えずに描画します。ビルド済み DLL で確認したサイズは次のとおりです。

| 表示と書体 | 16 ドット枠 | 20 ドット枠 | 24 ドット枠 | 32 ドット枠 |
|---|---:|---:|---:|---:|
| 単独・Regular | 12 px | **16 px** | 20 px | 28 px |
| 単独・Bold | 12 px | **12 px** | 20 px | 28 px |
| 2 段・オウムの数字・Regular | 12 px | 16 px | 18 px | 24 px |
| 2 段・オウムの数字・Bold | 12 px | 12 px | 18 px | 24 px |

**検証結果**　ビルドは警告 0・エラー 0。単体テストは追加 4 件を含む **1,166 件すべて合格**、スキップ 0 件でした。元の TTF とビルド済み DLL を使った原寸・画素・字間の照合も **4,856 件すべて合格**しました。PartsTray は起動しておらず、通知領域での見比べは実機確認に残しています。

SIL OFL 1.1 の全文が publish 出力と検証用 zip の両方に入り、元の `COPYING` とバイト単位で一致することを確認しました。設定値とライセンスの場所は [app/README.md](<../../app/README.md>) に記載しました。

[PartsTray.csproj](<../../app/PartsTray/PartsTray.csproj:16>) への追記は次の 3 行だけです。

```xml
<EmbeddedResource Include="Fonts\TerminusTTF-4.49.3.ttf" LogicalName="PartsTray.Fonts.TerminusTTF-4.49.3.ttf" />
<EmbeddedResource Include="Fonts\TerminusTTF-Bold-4.49.3.ttf" LogicalName="PartsTray.Fonts.TerminusTTF-Bold-4.49.3.ttf" />
<None Update="Fonts\Terminus-LICENSE.txt" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />
```