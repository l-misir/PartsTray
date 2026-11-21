// CPU 使用率・温度の変換と数字の点を定義します。
// 温度の有効範囲や数字の形を変えるときに開きます。
using System.Buffers.Binary;
using System.Globalization;

namespace PartsTray.Core;

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
public readonly record struct CpuTemperature(double Average, double Maximum);

public static class MonitorMath
{
    // measure は指定したピクセル数での点の幅・高さを返す。GDI+ に依存しない。
    public static float LargestFit(Func<float, (int Width, int Height)> measure, int size, float min, float max, float step)
    {
        ArgumentNullException.ThrowIfNull(measure);
        if (size <= 0 || !float.IsFinite(min) || !float.IsFinite(max) || !float.IsFinite(step) ||
            min <= 0 || max < min || step <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "正のサイズ・刻みと min <= max が必要です");
        for (double px = max; px > min; px -= step)
        {
            var (width, height) = measure((float)px);
            if (width <= size && height <= size) return (float)px;
        }
        return min;
    }

    public static double CpuUsage(CpuTimes previous, CpuTimes current, double previousUsage)
    {
        // decimal で差を取り、符号なし整数の巻き戻りや合計の桁あふれを避ける。
        decimal idle = (decimal)current.Idle - previous.Idle;
        decimal total = (decimal)current.Kernel - previous.Kernel + (decimal)current.User - previous.User;
        return Percent(total == 0 ? previousUsage : (double)(100 * (1 - idle / total)));
    }

    private static double Percent(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);
    // 11/19 実機：低負荷で「遅すぎてカクカク」とのことで、指示書J の式（500ms〜25ms）を 2 倍速にした。
    public static int FrameInterval(double cpuUsage) => (int)Math.Clamp(250 / Math.Max(1, Percent(cpuUsage) / 5), 20, 250);
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    // Windows の ACPI の温度（パフォーマンスカウンター「Thermal Zone Information / Temperature」、単位はケルビン）。
    // 温度の区域が複数あれば、いちばん高いものを使う（CPU の区域がふつう最も熱い）。平均と最高は同じ値にする。
    // 11/19 実機：区域は \_TZ.THRM の 1 つ。Core Temp の全コア平均より 5〜8℃ 高め、1℃ 刻み、負荷には約 2 秒で反応した。
    public static CpuTemperature? FromAcpi(IEnumerable<double> kelvins)
    {
        double? best = null;
        foreach (double k in kelvins)
        {
            double c = k - 273.15;
            if (double.IsNaN(c) || c < 20 || c >= 150) continue;
            if (best is null || c > best) best = c;
        }
        return best is { } t ? new CpuTemperature(t, t) : null;
    }

    public static CpuTemperature? ReadCoreTemp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2688) return null;
        uint cores = BinaryPrimitives.ReadUInt32LittleEndian(bytes[1536..]);
        uint cpus = BinaryPrimitives.ReadUInt32LittleEndian(bytes[1540..]);
        ulong count = (ulong)cores * cpus;
        // 要確認: CPU 数が TjMax 配列の 128 要素を超える構造体は、不正な共有メモリとして拒否する。
        if (count is 0 or > 256 || cpus > 128) return null;
        double sum = 0, maximum = 0;
        int valid = 0;
        for (int i = 0; i < (int)count; i++)
        {
            double t = BinaryPrimitives.ReadSingleLittleEndian(bytes[(1544 + i * 4)..]);
            if (bytes[2685] == 1)
                t = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(1024 + (int)((uint)i / cores) * 4)..]) - t;
            if (bytes[2684] == 1) t = (t - 32) * 5 / 9;
            if (!double.IsFinite(t) || t <= 0 || t >= 150) continue;
            sum += t; maximum = Math.Max(maximum, t); valid++;
        }
        return valid == 0 ? null : new CpuTemperature(sum / valid, maximum);
    }

    public static (int R, int G, int B) Rainbow(int frame, long elapsedMs)
    {
        double hue = (frame * 36 + elapsedMs / 10.0) % 360;
        double x = .9 * (1 - Math.Abs(hue / 60 % 2 - 1));
        var (r, g, b) = hue switch
        {
            < 60 => (.9, x, 0.0), < 120 => (x, .9, 0.0), < 180 => (0.0, .9, x),
            < 240 => (0.0, x, .9), < 300 => (x, 0.0, .9), _ => (.9, 0.0, x)
        };
        return (Round((r + .1) * 255), Round((g + .1) * 255), Round((b + .1) * 255));
    }
}

// 電池アイコンの絵を「ベタ塗り」にする（11/19 ユーザー：線だけの絵は見えにくい、特にヘッドホン）。
public static class PictureMask
{
    // 線の点（stroke）から、外側（端から点を通らずにたどれる所）を求め、それ以外を塗る。
    // ただし、外側に接していない線（キーボードのキー、マウスの真ん中の線など）は穴として残し、形がわかるようにする。
    public static bool[,] Solid(bool[,] stroke)
    {
        int h = stroke.GetLength(0), w = stroke.GetLength(1);
        var outside = new bool[h, w];
        var queue = new Queue<(int Y, int X)>();
        void Visit(int y, int x)
        {
            if (y < 0 || x < 0 || y >= h || x >= w || stroke[y, x] || outside[y, x]) return;
            outside[y, x] = true; queue.Enqueue((y, x));
        }
        for (int x = 0; x < w; x++) { Visit(0, x); Visit(h - 1, x); }
        for (int y = 0; y < h; y++) { Visit(y, 0); Visit(y, w - 1); }
        while (queue.Count > 0)
        {
            var (y, x) = queue.Dequeue();
            Visit(y + 1, x); Visit(y - 1, x); Visit(y, x + 1); Visit(y, x - 1);
        }
        bool NearOutside(int y, int x)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int yy = y + dy, xx = x + dx;
                    if (yy < 0 || xx < 0 || yy >= h || xx >= w || outside[yy, xx]) return true;
                }
            return false;
        }
        var result = new bool[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                result[y, x] = !outside[y, x] && !(stroke[y, x] && !NearOutside(y, x));
        return result;
    }
}

public static class MonitorFont
{
    public static bool[,] DeviceGlyph(BatteryDeviceKind kind, int height)
    {
        string dots = (kind, height) switch
        {
            (BatteryDeviceKind.Mouse, 5) => "01110/10101/11111/10001/01110",
            (BatteryDeviceKind.Mouse, 7) => "01110/10101/10101/11111/10001/10001/01110",
            (BatteryDeviceKind.Mouse, 9) => "0011100/0101010/1001001/1001001/1111111/1000001/1000001/0100010/0011100",
            (BatteryDeviceKind.Keyboard, 5) => "111111111/100000001/101010101/100111001/111111111",
            (BatteryDeviceKind.Keyboard, 7) => "11111111111/10000000001/10101010101/10000000001/10101010101/10011111001/11111111111",
            (BatteryDeviceKind.Keyboard, 9) => "1111111111111/1000000000001/1010101010101/1000000000001/1010101010101/1000000000001/1010111110101/1000000000001/1111111111111",
            (BatteryDeviceKind.Headphones, 5) => "0111110/1000001/1100011/1100011/0100010",
            (BatteryDeviceKind.Headphones, 7) => "001111100/010000010/100000001/100000001/111000111/111000111/011000110",
            (BatteryDeviceKind.Headphones, 9) => "00011111000/00100000100/01000000010/10000000001/10000000001/11100000111/11100000111/11100000111/01100000110",
            (_, 5) => "01110/11011/11011/10001/01110",
            (_, 7) => "0111110/1000001/1011101/1011101/1000001/1001001/0111110",
            (_, 9) => "011111110/100000001/100111001/100111001/100111001/100000001/100010001/100000001/011111110",
            _ => throw new ArgumentOutOfRangeException(nameof(height))
        };
        string[] rows = dots.Split('/');
        var pixels = new bool[height, rows[0].Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < rows[0].Length; x++) pixels[y, x] = rows[y][x] == '1';
        return pixels;
    }

    // 行優先のドット。拡大補間せず、それぞれの高さで定義する。
    private static readonly string[] Small =
    [
        "111/101/101/101/111", "010/110/010/010/111", "111/001/111/100/111",
        "111/001/111/001/111", "101/101/111/001/001", "111/100/111/001/111",
        "111/100/111/101/111", "111/001/010/010/010", "111/101/111/101/111",
        "111/101/111/001/111", "000/000/111/000/000"
    ];
    private static readonly string[] Medium =
    [
        "0110/1001/1001/1001/1001/1001/0110", "0010/0110/0010/0010/0010/0010/0111",
        "0110/1001/0001/0010/0100/1000/1111", "1110/0001/0001/0110/0001/0001/1110",
        "1001/1001/1001/1111/0001/0001/0001", "1111/1000/1000/1110/0001/0001/1110",
        "0111/1000/1000/1110/1001/1001/0110", "1111/0001/0010/0010/0100/0100/0100",
        "0110/1001/1001/0110/1001/1001/0110", "0110/1001/1001/0111/0001/0001/1110",
        "0000/0000/0000/1111/0000/0000/0000"
    ];
    private static readonly string[] Large =
    [
        "01110/10001/10001/10001/10001/10001/10001/10001/01110",
        "00100/01100/10100/00100/00100/00100/00100/00100/11111",
        "01110/10001/00001/00001/00110/01000/10000/10000/11111",
        "11110/00001/00001/00001/01110/00001/00001/00001/11110",
        "10001/10001/10001/10001/11111/00001/00001/00001/00001",
        "11111/10000/10000/10000/11110/00001/00001/00001/11110",
        "01111/10000/10000/10000/11110/10001/10001/10001/01110",
        "11111/00001/00001/00010/00100/00100/01000/01000/01000",
        "01110/10001/10001/10001/01110/10001/10001/10001/01110",
        "01110/10001/10001/10001/01111/00001/00001/00001/11110",
        "00000/00000/00000/00000/11111/00000/00000/00000/00000"
    ];

    public static int Width(int height) => height switch { 5 => 3, 7 => 4, 9 => 5, _ => throw new ArgumentOutOfRangeException(nameof(height)) };
    public static bool[,] Glyphs(string text, int height)
    {
        int width = Width(height);
        var font = height == 5 ? Small : height == 7 ? Medium : Large;
        var pixels = new bool[height, Math.Max(0, text.Length * (width + 1) - 1)];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '-' && text[i] is not (>= '0' and <= '9'))
                throw new ArgumentException("数字と - のみ描画できます", nameof(text));
            int glyph = text[i] == '-' ? 10 : text[i] - '0';
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) pixels[y, i * (width + 1) + x] = font[glyph][y * (width + 1) + x] == '1';
        }
        return pixels;
    }

    // 点の文字を横にだけ伸ばす。1 文字の幅を、横幅いっぱいに収まる範囲で広げる。
    // 増やす列は外側から（幅 5 → 9 なら列の幅は 2,2,1,2,2）。縦線が太くなり、形は崩れない。
    public static bool[,] GlyphsWide(string text, int height, int maxWidth, int minimumDigits = 2)
    {
        var narrow = Glyphs(text, height);
        int baseWidth = Width(height), n = text.Length;
        if (n == 0) return narrow;
        // 通常は2桁、uniformでは3桁の字幅を短い数字にも使う。
        int slots = Math.Max(n, minimumDigits);
        // 1 文字の幅は高さまで（縦横比 1:1 まで）。高さ 7 の CPU 使用率を幅 9 まで伸ばすと、
        // 「横に広がりすぎて読みづらい・温度と比率が違って違和感がある」とのこと（11/19）。
        int target = Math.Max(baseWidth, Math.Min(height, (maxWidth - (slots - 1)) / slots));
        if (target == baseWidth) return narrow;
        int[] widths = ColumnWidths(baseWidth, target);
        var pixels = new bool[height, n * target + (n - 1)];
        for (int i = 0; i < n; i++)
        {
            int x = i * (target + 1);
            for (int column = 0; column < baseWidth; column++)
                for (int repeat = 0; repeat < widths[column]; repeat++, x++)
                    for (int y = 0; y < height; y++)
                        pixels[y, x] = narrow[y, i * (baseWidth + 1) + column];
        }
        return pixels;
    }
    public static int[] ColumnWidths(int baseWidth, int target)
    {
        var widths = Enumerable.Repeat(1, baseWidth).ToArray();
        // 外側から内側へ：0, 最後, 1, 最後-1, …
        var order = new List<int>();
        for (int left = 0, right = baseWidth - 1; left <= right; left++, right--)
        { order.Add(left); if (right != left) order.Add(right); }
        for (int extra = target - baseWidth, k = 0; extra > 0; extra--, k++) widths[order[k % order.Count]]++;
        return widths;
    }

    public static int SelectHeight(int size, int rowHeight, int digits = 2)
    {
        foreach (int h in new[] { 9, 7, 5 })
            if (h <= rowHeight && Width(h) * digits + digits - 1 <= size) return h;
        throw new ArgumentOutOfRangeException(nameof(size), $"{digits} 桁を描く領域が不足しています");
    }
    public static int TempMemoryHeight(int size) => SelectHeight(size, (size - 1) / 2);
    // オウム・電池の数字の高さ。"thin"（Core Temp 風）は温度・メモリと同じ高さにそろえる。
    public static int SmallTextHeight(int size, string style) => style == "thin" ? TempMemoryHeight(size) : ParrotHeight(size);
    // 数字の点。"thin" は伸ばさない（線 1 ドット）、"wide" は横に伸ばす。
    public static bool[,] Digits(string text, int height, int maxWidth, string style) =>
        style == "thin" ? Glyphs(text, height) : GlyphsWide(text, height, maxWidth);
    // 11/19 実機：20px で高さ 5 の数字は「ほぼ見えない」とのことで、20px 以上は 7（32px 以上は 9）にした。
    public static int ParrotHeight(int size) => SelectHeight(size, size >= 32 ? 9 : size >= 20 ? 7 : 5);
    public static string FitNumber(int value, int size, int height)
    {
        // 要確認: 高さ 9 の 100 は幅 17 なので 20px ではそのまま表示し、実際に幅が足りない場合だけ 99 にする。
        string text = value.ToString(CultureInfo.InvariantCulture);
        return value >= 100 && text.Length * (Width(height) + 1) - 1 > size ? "99" : text;
    }
}
