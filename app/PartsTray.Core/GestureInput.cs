// ジェスチャーの入力・効果の型と軌跡の方向認識を定義します。
// 入力の種類やジェスチャーの形の認識を変えるときに開きます。
namespace PartsTray.Core;

public readonly record struct PixelPoint(int X, int Y);
public enum MouseEventKind { Move, Down, Up, Wheel, HorizontalWheel }
public enum PointerButton { Left, Right, Middle }
[Flags] public enum PointerModifiers { None = 0, Shift = 1, Control = 2 }
public readonly record struct PointerEvent(MouseEventKind Kind, PixelPoint Position,
    PointerButton Button = PointerButton.Left, int Delta = 0, PointerModifiers Modifiers = 0, nuint ExtraInfo = 0);
public enum GestureEffectKind { HitTest, Probe, Replay, Command, Focus, MoveWindow, TrailBegin, TrailPoint, TrailShow, TrailEnd, Tooltip, Warning, Flush, SwitchMaximized, RightWheelReport, EdgeRedirectReport, FastScroll }
public readonly record struct GestureEffect(GestureEffectKind Kind, long Token = 0, PointerEvent Input = default,
    long Window = 0, string? Text = null, bool Valid = false, TimeSpan At = default, int Steps = 0,
    EdgeWheelTarget Target = default, WheelWindow WheelWindow = default, int OriginalDelta = 0)
{
    // 入力ワーカーで窓や設定が変わっていたら、取り込んだ元のホイールを等倍で1回返す。
    public (PointerEvent Input, EdgeWheelTarget Target) FastWheelDelivery(WheelSettings settings,
        long root, uint processId, string? processName, bool targetCurrent)
    {
        bool allowed = root != 0 && root == WheelWindow.Root && processId != 0 && processId == WheelWindow.ProcessId &&
            settings.FastScrollOnScrollBar && settings.AllowsFastScroll(processName) &&
            (Target.Window == 0 || settings.EdgeAssist && targetCurrent);
        return allowed ? (Input, Target) : (Input with { Delta = OriginalDelta }, default);
    }
}
public readonly record struct HitResult(long Window, bool AllowGesture, string? Command = null);
public readonly record struct WheelWindow(long Root, uint ProcessId, PixelPoint Point, long Source = 0);
public readonly record struct PointerCache(PixelPoint Position, TimeSpan At, bool VerticalScrollBar, long DisabledWindow = 0,
    PixelPoint WindowOrigin = default, bool EdgeScrollBar = false, EdgeWheelTarget Redirect = default,
    EdgeWheelTarget ChromiumEdge = default, WheelWindow WheelWindow = default, bool WheelExcluded = false);
public interface IGestureEffects
{
    void Post(GestureEffect effect);
    EdgeWheelTarget FirstChromiumEdge(PixelPoint point, WheelSettings wheel) => default;
    bool IsCurrentWheelWindow(WheelWindow window) => true;
    // フックから待たずに投函し、成功したときだけ元の入力を取り込む。
    bool TryRedirectWheel(PointerEvent input, EdgeWheelTarget target) => false;
}
public interface IGestureHitTest
{
    HitResult HitTest(PointerEvent input, AppSettings settings);
    PointerCache Probe(PixelPoint point, TimeSpan at);
}
public interface IGestureInput { bool Replay(PointerEvent input); }

public sealed class StrokeRecognizer(PixelPoint start, GestureSettings settings)
{
    private PixelPoint anchor = start;
    private char last;
    private readonly System.Text.StringBuilder strokes = new();
    private string lookup = "";
    private readonly int maximum = settings.Gestures.Keys.Select(k => k.Length).DefaultIfEmpty(0).Max();
    public string Pattern => strokes.ToString();
    public bool HasStroke => last != '\0';
    public string? Command => settings.Gestures.GetValueOrDefault(lookup);
    public bool IsValid => Command != null;
    public string Description => Pattern.Replace("L", "←").Replace("R", "→").Replace("U", "↑").Replace("D", "↓") + " " +
        (Command switch { "Back" => "戻る", "Forward" => "進む", "Refresh" => "更新", "Close" => "閉じる", "Minimize" => "最小化", _ => "（未登録）" });
    public void Move(PixelPoint p)
    {
        long dx = (long)p.X - anchor.X, dy = (long)p.Y - anchor.Y;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < settings.StrokeDistancePx) return;
        // 要確認: 斜め45度で両軸が同距離の場合は横方向を優先する。
        char direction = Math.Abs(dx) >= Math.Abs(dy) ? dx < 0 ? 'L' : 'R' : dy < 0 ? 'U' : 'D';
        anchor = p;
        if (direction == last) return;
        last = direction;
        strokes.Append(direction);
        // A sequence longer than every binding can never become valid again. Bound hook allocations.
        if (lookup.Length <= maximum) lookup += direction;
    }
}
