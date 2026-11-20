// JSON 設定の既定値と入力値の検査を定義します。
// 設定項目を追加するときは既存キーの意味を保ってここを編集します。
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartsTray.Core;

public sealed record AppSettings
{
    public GapSettings GapFiller { get; init; } = new();
    public GestureSettings Gesture { get; init; } = new();
    public TitleBarSettings TitleBar { get; init; } = new();
    public WheelSettings Wheel { get; init; } = new();
    public DiagnosticsSettings Diagnostics { get; init; } = new();
    public MonitorSettings Monitor { get; init; } = new();
    public BatterySettings Battery { get; init; } = new();
    public bool MoveDisabledWindows { get; init; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Modules { get; init; }
    public static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) Save(path, new());
        var result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("設定が空です");
        result.Validate();
        return result;
    }
    public static void Save(string path, AppSettings value)
    {
        value.Validate();
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
    public void Validate()
    {
        if (Battery is null) throw new InvalidDataException("battery が必要です");
        Battery.Validate();
        if (Monitor is null) throw new InvalidDataException("monitor が必要です");
        Monitor.Validate();
        if (Diagnostics is null) throw new InvalidDataException("diagnostics が必要です");
        GestureSettings.Validate(this);
        var g = GapFiller ?? throw new InvalidDataException("gapFiller が必要です");
        if (g.MouseWpid != "0xB042" || g.OwnSwid != 5)
            throw new InvalidDataException("安全のため WPID=B042 / swid=5 は固定です");
        if (g.Dpi is null || !ValidDpi(g.Dpi.Primary) || !ValidDpi(g.Dpi.Secondary))
            throw new InvalidDataException("DPI は 200〜8000、50 刻みです");
        if (g.ThumbWheel is null || g.ThumbWheel.DirectionSign is not (1 or -1) ||
            g.ThumbWheel.SpeedPercent is < 0 or > 300 || g.CleanupDelaySeconds is < 15 or > 300)
            throw new InvalidDataException("ホイール設定または後片付け時間が不正です");
        if (g.ThumbWheel.Mode is not ("hook" or "divert" or "off"))
            throw new InvalidDataException("thumbWheel.mode は hook / divert / off のどれかです");
        if (g.ThumbWheel.SmoothMode is not ("linear" or "spring"))
            throw new InvalidDataException("thumbWheel.smoothMode は linear / spring のどちらかです");
        if (g.ThumbWheel.SpringMs is < 10 or > 500)
            throw new InvalidDataException("thumbWheel.springMs は 10〜500 です");
        if (g.ThumbWheel.SmoothMs is < 0 or > 200 || g.ThumbWheel.SmoothIntervalMs is < 4 or > 50 ||
            (g.ThumbWheel.SmoothMs != 0 && g.ThumbWheel.SmoothMs < g.ThumbWheel.SmoothIntervalMs))
            throw new InvalidDataException("thumbWheel.smoothMs は 0〜200、smoothIntervalMs は 4〜50 で、smoothMs が 0 でなければ smoothIntervalMs 以上です");
        if (g.Buttons is null) throw new InvalidDataException("buttons が必要です");
        foreach (var (cid, action) in g.Buttons)
        {
            if (!GapSession.ButtonOrder.Any(c => $"0x{c:X4}" == cid) || action is null ||
                (action.Action != "ToggleDpi" && (action.Action != "Keys" ||
                action.Keys is not ("Ctrl+C" or "Ctrl+V" or "PrintScreen" or "Win+Up"))))
                throw new InvalidDataException($"未対応のボタン設定: {cid}");
        }
    }
    private static bool ValidDpi(int dpi) => dpi is >= 200 and <= 8000 && dpi % 50 == 0;
}

public sealed record BatterySettings
{
    public bool Enabled { get; init; } = true;
    public bool Logitech { get; init; } = true;
    public bool Bluetooth { get; init; } = true;
    public int LowWarn { get; init; } = 20;
    public int LowHot { get; init; } = 10;
    public List<string> Hidden { get; init; } = [];
    public Dictionary<string, string> Kinds { get; init; } = new(StringComparer.Ordinal);
    public int RefreshMinutes { get; init; } = 10;
    public int BluetoothPollSeconds { get; init; } = 60;
    // 機器の絵の高さ（20 ドットのアイコンでのドット数。ほかの大きさでは同じ割合で伸び縮みする）。
    // 11/19 ユーザー：全面（20）より小さく、上下に分けた形（12）より大きく。見本の 14〜17 のうち、既定は 15。
    public int PictureHeight { get; init; } = 15;
    // 残量 100% の機器は、輪を虹色にして回す（11/20 ユーザー：「パーティーパロットが虹色に光るなら、こっちも虹色に光ればええやん」）。
    // RainbowLapMs は虹が 1 周する時間。見本（1.9 秒）の 1.5〜2 倍の速さにしてほしい、とのことで既定は 1100。
    public bool RainbowWhenFull { get; init; } = true;
    public int RainbowLapMs { get; init; } = 1100;

    public void Validate()
    {
        if (RainbowLapMs is < 200 or > 10000)
            throw new InvalidDataException("battery.rainbowLapMs は 200〜10000 です");
        if (Kinds is null || Kinds.Any(k => string.IsNullOrWhiteSpace(k.Key) || !BatteryKinds.TryParse(k.Value, out _)))
            throw new InvalidDataException("battery.kinds は機器名と mouse / keyboard / headphones / earbuds / gamepad / other の組です");
        if (Hidden is null) throw new InvalidDataException("battery.hidden が必要です");
        if (PictureHeight is < 8 or > 20)
            throw new InvalidDataException("battery.pictureHeight は 8〜20 です");
        if (LowHot is < 1 or > 100 || LowWarn is < 1 or > 100 || LowHot >= LowWarn)
            throw new InvalidDataException("battery.lowHot / lowWarn は 1〜100、lowHot < lowWarn です");
        if (RefreshMinutes is < 1 or > 120 || BluetoothPollSeconds is < 10 or > 600)
            throw new InvalidDataException("battery.refreshMinutes は 1〜120、bluetoothPollSeconds は 10〜600 です");
    }
}

public sealed record GapSettings
{
    public bool Enabled { get; init; } = false;
    public bool NotifyOnSwitch { get; init; } = true;
    public string MouseWpid { get; init; } = "0xB042";
    public int OwnSwid { get; init; } = 5;
    // optionsPlusSwid は廃止。Options+ の swid は起動ごとに変わるため、0・5 以外の応答をすべて他のソフトとして扱う。
    // 古い config.json に残っていても、読み込みで無視される。
    public DpiSettings Dpi { get; init; } = new();
    public Dictionary<string, ButtonSettings> Buttons { get; init; } = new()
    {
        ["0x0052"] = new() { Action = "ToggleDpi" },
        ["0x0056"] = new() { Keys = "Ctrl+C" },
        ["0x0053"] = new() { Keys = "Ctrl+V" },
        ["0x00C3"] = new() { Keys = "PrintScreen" },
        ["0x01A0"] = new() { Keys = "Win+Up" }
    };
    public ThumbSettings ThumbWheel { get; init; } = new();
    public int CleanupDelaySeconds { get; init; } = 15;
}
public sealed record DpiSettings { public int Primary { get; init; } = 2200; public int Secondary { get; init; } = 1100; }
public sealed record ButtonSettings { public string Action { get; init; } = "Keys"; public string? Keys { get; init; } }
public sealed record ThumbSettings
{
    public string Mode { get; init; } = "hook";
    public decimal SpeedPercent { get; init; } = 50;
    // 1 = Options+ の「反転」オンと同じ向き。-1 = その逆。
    public int DirectionSign { get; init; } = 1;
    // 肩代わり中（hook）の 1 段ぶんを、この時間（ms）の間に分けて送る。0 = すぐ全部送る。
    public int SmoothMs { get; init; } = 40;
    // 分けて送る間隔（ms）。
    public int SmoothIntervalMs { get; init; } = 8;
    // 肩代わり中の滑らか化の方式。linear = 均等割り、spring = バネの動き。
    public string SmoothMode { get; init; } = "linear";
    // spring のバネの時間（ms）。大きいほど、ゆっくり動き出して、ゆっくり止まる。
    public int SpringMs { get; init; } = 50;
}

public sealed record DiagnosticsSettings
{
    // true のとき、フックが見た横スクロールを logs/wheel_yyyyMMdd.log に記録する。
    public bool WheelTrace { get; init; } = false;
}

public sealed record MonitorSettings
{
    public string TaskbarTheme { get; init; } = "auto";
    public bool Enabled { get; init; } = true;
    public ParrotSettings Parrot { get; init; } = new();
    public CpuSettings Cpu { get; init; } = new();
    public TempMemorySettings TempMemory { get; init; } = new();
    // 11/19 実機：1000ms では「明らかに数値の変化が遅い（5〜10 倍）」とのことで、既定を 200ms にした。
    public int SampleIntervalMs { get; init; } = 200;
    // 数字の形（全部のアイコンで共通）。
    // "wide" = 点の文字を横に伸ばす。単独の数字はアイコン全体に収まる高さを選ぶ。
    // "thin" = Tahoma をにじませず描く。単独の数字は「88」が収まる最大サイズ。
    //          2 段表示・オウム・電池に重ねる数字は従来の小さいサイズを維持する。
    public string DigitStyle { get; init; } = "wide";

    public void Validate()
    {
        if (TaskbarTheme is not ("auto" or "dark" or "light"))
            throw new InvalidDataException("monitor.taskbarTheme は auto / dark / light のどれかです");
        if (Parrot is null) throw new InvalidDataException("monitor.parrot が必要です");
        if (Cpu is null) throw new InvalidDataException("monitor.cpu が必要です");
        if (!MonitorColor.TryParse(Cpu.Color, out _) || Cpu.Color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
            throw new InvalidDataException("monitor.cpu.color は #RRGGBB の形です");
        if (TempMemory is null) throw new InvalidDataException("monitor.tempMemory が必要です");
        if (SampleIntervalMs is < 100 or > 5000)
            throw new InvalidDataException("monitor.sampleIntervalMs は 100〜5000 です");
        if (DigitStyle is not ("wide" or "thin"))
            throw new InvalidDataException("monitor.digitStyle は wide / thin のどちらかです");
        if (Parrot.WidenPx is < 0 or > 5)
            throw new InvalidDataException("monitor.parrot.widenPx は 0〜5 です");
        if (Parrot.CpuText is not ("bottom" or "top" or "none"))
            throw new InvalidDataException("monitor.parrot.cpuText は bottom / top / none です");
        if (!MonitorColor.TryParse(Parrot.CpuTextColor, out _))
            throw new InvalidDataException("monitor.parrot.cpuTextColor は #RRGGBB の形です");
        if (TempMemory.Source is not ("acpi" or "coretemp"))
            throw new InvalidDataException("monitor.tempMemory.source は acpi / coretemp のどちらかです");
        if (!MonitorColor.TryParse(TempMemory.TempColor, out _))
            throw new InvalidDataException("monitor.tempMemory.tempColor は #RRGGBB の形です");
        if (!MonitorColor.TryParse(TempMemory.MemoryColor, out _))
            throw new InvalidDataException("monitor.tempMemory.memoryColor は #RRGGBB の形です");
    }
}

public sealed record ParrotSettings
{
    public bool Enabled { get; init; } = true;
    public string CpuText { get; init; } = "none";
    // 11/20 ユーザーの指定：絵が横長（296×223）で正方形のアイコンでは小さいので、縦にだけ伸ばして枠いっぱいにする（20px で 20×15 → 20×20）。
    // 横幅はそのまま（左右を切ると、左右に振る頭がコマによって切れるため）。false で縦横比を保つ。
    public bool Stretch { get; init; } = true;
    // 11/20 ユーザーの指定：横にも左右それぞれこのドット数だけ伸ばす（20px なら幅 22 で描く）。
    // アイコンからはみ出た分は切れるが、各コマの絵は全コマの範囲より狭いので、切れるのは頭を一番振ったコマの端だけ。0〜5。
    public int WidenPx { get; init; } = 1;
    // 色の塗り替え。partyparrot.gif（もともと虹色）があるときは使わず、RunCat365 の影絵を使うときだけ効く。
    public bool Rainbow { get; init; } = true;
    public string CpuTextColor { get; init; } = "#FFFFFF";
}

public sealed record CpuSettings
{
    public bool Enabled { get; init; } = true;
    public string Color { get; init; } = "#64D2FF";
}

// 11/19 ユーザーの指定：CPU 温度は赤、メモリ使用率は白（タスクバーは黒）。しきい値で色を変える作りはやめた。
public sealed record TempMemorySettings
{
    public bool Enabled { get; init; } = true;
    // true = 温度とメモリを別々の大きな数字にする。false = 従来の 2 段。
    public bool Split { get; init; } = true;
    // CPU 温度の読み方。"acpi" = Windows の ACPI の温度（Core Temp 不要。11/19 ユーザーの選択で既定）、
    // "coretemp" = Core Temp の共有メモリ（全コアの平均。Core Temp を動かしておく必要がある）。
    public string Source { get; init; } = "acpi";
    public string TempColor { get; init; } = "#FF3B30";
    public string MemoryColor { get; init; } = "#FFFFFF";
}

public static class MonitorColor
{
    public static bool TryParse(string? text, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' ||
            !int.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int value))
            return false;
        color = ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}
