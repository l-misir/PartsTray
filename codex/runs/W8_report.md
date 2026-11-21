[見本「CoreTemp風の比べ.png」を開く](<../../codex/見本/数字100/CoreTemp風の比べ.png>)

**20ドット枠では、②の `Tahoma, Regular, Pixel` は①のCore Temp 8ポイント相当と、見本の7値すべてで同じ画素になりました。** `uniform` が選んだサイズは13.5 pxですが、「0」の高さはどちらも9ドットです。16ドット枠でも高さはどちらも8ドットで、`100` と `150` の字間だけが変わりました。③のTerminusは中央の線が消え、20ドット枠では「0」が10ドット高になります。

共通・CPU・温度・メモリの4つのフォント設定で、`Regular`／`Bold` と `Pixel`／`Smooth` を指定できます。書体名だけの従来の指定も使えます。④には現行の Tahoma 太字をそのまま載せました。既存実装はにじみなしで、`Smooth` を明示すると輪郭に濃淡が付きます。`plainZero` は既定で `true`、`false` で元の「0」に戻せます。既定フォントと実機の `config.json` は変更していません。

**設定例**　CPUと温度を②にし、メモリは今のTerminus太字を使う場合は、`config.json` の `monitor` 内で次の値を指定します。空文字列の項目は共通の `digitFont` を使います。

```json
{
  "digitStyle": "thin",
  "hundredStyle": "uniform",
  "digitFont": "Tahoma, Regular, Pixel",
  "cpuDigitFont": "",
  "temperatureDigitFont": "",
  "memoryDigitFont": "TerminusBold",
  "plainZero": true
}
```

ビルドは警告・エラーともに0件、**単体テストは1,231件すべて合格**しました。アプリを起動せずに行った描画検証も40,312件すべて合格し、16・20ドットの各枠で0〜150の全値が字間1ドットで収まることを確認しました。