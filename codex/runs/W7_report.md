CPU 使用率・CPU 温度・メモリ使用率のフォントを個別に指定できるようにしました。オウムに重ねる数字は CPU 使用率と同じフォントを使います。2段表示では、上段の温度と下段のメモリがそれぞれの書体で大きさを選びます。

**設定例**です。既存の `config.json` の `monitor` に次の値を指定すると、CPU は `Bahnschrift SemiBold`、温度は Terminus Regular、メモリは Terminus Bold になります。

```json
{
  "digitStyle": "thin",
  "cpuDigitFont": "Bahnschrift SemiBold",
  "temperatureDigitFont": "Terminus",
  "memoryDigitFont": "TerminusBold"
}
```

3項目の既定値は `""` です。空または省略なら `monitor.digitFont` を使うので、**CPU を今の書体にするには `cpuDigitFont` を `""`** にしてください。

**そのまま書けるフォント名**

- 同梱のドットフォントは `Terminus`（Regular）、`TerminusBold`（Bold）です。
- このPCで確認した Windows フォントは `Tahoma`、`Bahnschrift SemiBold`、`Bahnschrift`、`Segoe UI`、`Segoe UI Semibold`、`Verdana`、`Arial`、`Consolas`、`Yu Gothic UI`、`Meiryo UI` です。**`Bahnschrift SemiBold` は半角スペース1つ**です。

`uniform` の字間は1ドットです。Terminus は 0〜150 が収まる最大の原寸を選ぶため、20ドットの単独アイコンでは Regular が 16 px、Bold が 12 px になります。

**検証結果**は、ビルドが警告・エラーともに0件、テストが **1,170件すべて合格**です。アプリを起動せずに行った描画検証も **4,868件すべて合格**しました。うち12件で、2段の書体を両方指定した場合と片方だけ空にした場合の画素を、16・20・24・32ドットで照合しました。