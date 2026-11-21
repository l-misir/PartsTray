// ジェスチャー・タイトルバー・ホイールの設定と検査を定義します。
// ジェスチャーの既定値や設定項目を変えるときに開きます。
namespace PartsTray.Core;

public sealed record GestureSettings
{
    // 11/20：配った人の PC で、知らないうちにウィンドウの動きが変わらないよう、かざぐるマウス由来の操作は最初は切る（設定画面でオンにできる）。
    public bool Enabled { get; init; } = false;
    public bool RightWheelSwitch { get; init; } = false;
    public int StrokeDistancePx { get; init; } = 10;
    public int StartTimeoutMs { get; init; } = 1000;
    public int TooltipDelayMs { get; init; } = 1000;
    public bool RapidFire { get; init; } = true;
    public TrailSettings Trail { get; init; } = new();
    public Dictionary<string, string> Gestures { get; init; } = new()
    { ["L"] = "Back", ["R"] = "Forward", ["UD"] = "Refresh", ["DR"] = "Close", ["DL"] = "Minimize" };
    public string[] BrowserProcesses { get; init; } = ["chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "vivaldi.exe", "opera.exe"];
    public string[] ExcludedProcesses { get; init; } = [];
    public bool ExplorerSkipSelectedItems { get; init; } = true;
    public static void Validate(AppSettings settings)
    {
        var g = settings.Gesture;
        if (g == null || g.StrokeDistancePx is < 1 or > 1000 || g.StartTimeoutMs is < 1 or > 2000 ||
            g.TooltipDelayMs is < 1 or > 60000 || g.Trail == null || !float.IsFinite(g.Trail.WidthPx) ||
            g.Trail.WidthPx is < 1 or > 100 || !Color(g.Trail.ValidColor) || !Color(g.Trail.InvalidColor) ||
            g.Gestures == null || g.Gestures.Count > 256 || g.BrowserProcesses == null || g.ExcludedProcesses == null)
            throw new InvalidDataException("gesture 設定が不正です");
        foreach (var (pattern, command) in g.Gestures)
            if (pattern.Length is < 1 or > 64 || pattern.Any(c => c is not ('L' or 'R' or 'U' or 'D')) ||
                pattern.Zip(pattern.Skip(1)).Any(p => p.First == p.Second) ||
                command is not ("Back" or "Forward" or "Refresh" or "Close" or "Minimize"))
                throw new InvalidDataException("未対応のジェスチャーです");
        if (g.BrowserProcesses.Concat(g.ExcludedProcesses).Any(p => string.IsNullOrWhiteSpace(p) || p.IndexOfAny(['/', '\\', ':']) >= 0))
            throw new InvalidDataException("プロセス名だけを指定してください");
        var t = settings.TitleBar;
        if (t == null || t.ShiftClick is not ("ToggleTopMost" or "None") || t.CtrlClick is not ("OpenExecutableFolder" or "None") ||
            t.MiddleClick is not ("CloseWindow" or "None") || t.MinimizeButtonRightClick is not ("MinimizeToTray" or "None"))
            throw new InvalidDataException("titleBar 設定が不正です");
        if (settings.Wheel == null || settings.Wheel.FastScrollMultiplier is < 1 or > 100)
            throw new InvalidDataException("wheel 設定が不正です");
    }
    private static bool Color(string? s) => s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}
public sealed record TrailSettings
{
    public bool Enabled { get; init; } = true;
    public float WidthPx { get; init; } = 3;
    public string ValidColor { get; init; } = "#00FF00";
    public string InvalidColor { get; init; } = "#FF0000";
}
public sealed record TitleBarSettings
{
    // 11/20：配った人の PC で、知らないうちにウィンドウの動きが変わらないよう、かざぐるマウス由来の操作は最初は切る（設定画面でオンにできる）。
    public string ShiftClick { get; init; } = "None";
    public string CtrlClick { get; init; } = "None";
    public string MiddleClick { get; init; } = "None";
    public string MinimizeButtonRightClick { get; init; } = "None";
}
public sealed record WheelSettings
{
    // 11/20：配った人の PC で、知らないうちにウィンドウの動きが変わらないよう、かざぐるマウス由来の操作は最初は切る（設定画面でオンにできる）。
    public bool FastScrollOnScrollBar { get; init; } = false;
    public int FastScrollMultiplier { get; init; } = 5;
    public bool FocusUnderCursor { get; init; } = false;
}
