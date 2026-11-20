namespace PartsTray.Core;

public readonly record struct PixelPoint(int X, int Y);
public enum MouseEventKind { Move, Down, Up, Wheel, HorizontalWheel }
public enum PointerButton { Left, Right, Middle }
[Flags] public enum PointerModifiers { None = 0, Shift = 1, Control = 2 }
public readonly record struct PointerEvent(MouseEventKind Kind, PixelPoint Position,
    PointerButton Button = PointerButton.Left, int Delta = 0, PointerModifiers Modifiers = 0, nuint ExtraInfo = 0);
public enum GestureEffectKind { HitTest, Probe, Replay, Command, Focus, MoveWindow, TrailBegin, TrailPoint, TrailShow, TrailEnd, Tooltip, Warning, Flush }
public readonly record struct GestureEffect(GestureEffectKind Kind, long Token = 0, PointerEvent Input = default,
    long Window = 0, string? Text = null, bool Valid = false, TimeSpan At = default);
public readonly record struct HitResult(long Window, bool AllowGesture, string? Command = null);
public readonly record struct PointerCache(PixelPoint Position, TimeSpan At, bool VerticalScrollBar, long DisabledWindow = 0,
    PixelPoint WindowOrigin = default);
public interface IGestureEffects { void Post(GestureEffect effect); }
public interface IGestureHitTest
{
    HitResult HitTest(PointerEvent input, AppSettings settings);
    PointerCache Probe(PixelPoint point, TimeSpan at);
}
public interface IGestureInput { bool Replay(PointerEvent input); }

/// <summary>Single owner (hook message thread). No native calls, waits, drawing, or external callbacks
/// except the nonblocking effect queue. Worker replies return to that same owner by token.</summary>
public sealed class GestureEngine(IClock clock, IGestureEffects effects, AppSettings settings)
{
    private enum Phase { Pending, Gesture, Consumed, Replaying, Passed, Drag }
    private sealed class Capture(long token, PointerEvent down, TimeSpan at, AppSettings settings)
    {
        public readonly long Token = token;
        public readonly PointerEvent Down = down;
        public readonly TimeSpan At = at;
        public readonly AppSettings Settings = settings;
        public Phase Phase;
        public PointerEvent? Up;
        public long Window;
        public StrokeRecognizer Stroke = new(down.Position, settings.Gesture);
        public bool RapidExecuted;
        public PixelPoint Origin;
        public long ParentGesture;
        public string? RapidCommand;
    }
    private readonly Capture?[] buttons = new Capture?[3];
    private readonly Dictionary<long, Capture> pending = [];
    private readonly List<Capture> expired = [];
    private AppSettings settings = settings;
    private long nextToken;
    private PointerCache? cache;
    private TimeSpan nextProbe, refreshProbeAt, wheelDisabledUntil;
    private TimeSpan? probeActiveUntil;
    private PixelPoint position, stillPoint;
    private TimeSpan stillSince;
    private bool paused, tip, probePending;
    public bool Paused => paused;
    public void UpdateSettings(AppSettings value) { SetPaused(true); settings = value; SetPaused(false); }
    public void SetPaused(bool value)
    {
        paused = value;
        if (!value) return;
        foreach (var c in pending.Values.ToArray()) Replay(c);
        foreach (var c in buttons)
        {
            if (c == null) continue;
            if (c.Phase == Phase.Gesture)
            {
                if (c.Stroke.HasStroke) c.Phase = Phase.Consumed;
                else Replay(c);
            }
            if (c.Phase == Phase.Drag) c.Phase = Phase.Consumed;
        }
        Emit(GestureEffectKind.TrailEnd);
        cache = null;
        probePending = false;
        probeActiveUntil = null;
    }
    public void HookLost()
    {
        // Input during the missing hook has already reached Windows. Never replay it here.
        Array.Clear(buttons); pending.Clear(); expired.Clear();
        cache = null; tip = false; probePending = false; probeActiveUntil = null;
        Emit(GestureEffectKind.TrailEnd);
    }
    private void DiscardMissedRelease(Capture c)
    {
        buttons[(int)c.Down.Button] = null;
        pending.Remove(c.Token);
        if (c.Phase == Phase.Replaying)
            Emit(GestureEffectKind.Replay, c, new(MouseEventKind.Up, position, c.Down.Button));
        if (c.Down.Button == PointerButton.Right)
        {
            Emit(GestureEffectKind.TrailEnd, c); tip = false;
            // 要確認: 取りこぼした右押下に従属する未確定の連続実行も破棄し、保持中の左解放だけ消費する。
            foreach (var child in pending.Values.Where(x => x.ParentGesture == c.Token).ToArray())
            { pending.Remove(child.Token); child.Phase = Phase.Consumed; }
        }
        Emit(GestureEffectKind.Warning, c, text: "ボタン解放の取りこぼし: 古い記録を破棄して新しい押下を処理");
    }
    public void Shutdown()
    {
        SetPaused(true);
        foreach (var c in buttons)
        {
            if (c == null || c.Phase is not (Phase.Replaying or Phase.Passed)) continue;
            Emit(GestureEffectKind.Replay, c, new(MouseEventKind.Up, position, c.Down.Button));
            c.Phase = Phase.Consumed;
        }
    }
    public void SetCache(PointerCache value) => cache = value;
    private bool Fresh(PixelPoint p) => cache is { } c && c.Position == p && clock.Now >= c.At &&
        clock.Now - c.At <= TimeSpan.FromMilliseconds(100);
    private void Emit(GestureEffectKind kind, Capture? c = null, PointerEvent input = default, string? text = null, bool valid = false)
        => effects.Post(new(kind, c?.Token ?? 0, input, c?.Window ?? 0, text, valid, clock.Now));

    public bool Handle(PointerEvent e)
    {
        // This check precedes even timers/cache updates: includes the gap filler's own input.
        if (e.ExtraInfo == KeyStroke.ExtraInfo) return false;
        position = e.Position;
        // Do this before Tick: an expired stale capture must not replay its old down.
        if (e.Kind == MouseEventKind.Down && buttons[(int)e.Button] is { } stale) DiscardMissedRelease(stale);
        Tick();
        if (e.Kind == MouseEventKind.Up)
        {
            var c = buttons[(int)e.Button];
            if (c == null) return false;
            buttons[(int)e.Button] = null; c.Up = e;
            if (e.Button == PointerButton.Right) Emit(GestureEffectKind.TrailEnd, c);
            if (c.Phase == Phase.Pending) return true;
            if (c.Phase == Phase.Gesture) { FinishGesture(c); return true; }
            if (c.Phase == Phase.Replaying) Emit(GestureEffectKind.Replay, c, e);
            return c.Phase != Phase.Passed;
        }
        if (paused) return false;
        if (e.Kind == MouseEventKind.Move)
        {
            RequestProbe();
            if (buttons[(int)PointerButton.Left] is { Phase: Phase.Drag } drag)
            {
                Emit(GestureEffectKind.MoveWindow, drag, e with { Position = new(
                    drag.Origin.X + e.Position.X - drag.Down.Position.X,
                    drag.Origin.Y + e.Position.Y - drag.Down.Position.Y) });
                return false;
            }
            if (buttons[(int)PointerButton.Right] is { } right && right.Phase is Phase.Pending or Phase.Gesture)
            {
                right.Stroke.Move(e.Position);
                Emit(GestureEffectKind.TrailPoint, right, e, valid: right.Stroke.IsValid);
                if (right.Phase == Phase.Gesture && right.Stroke.HasStroke)
                    Emit(GestureEffectKind.TrailShow, right, valid: right.Stroke.IsValid);
                long dx = (long)e.Position.X - stillPoint.X, dy = (long)e.Position.Y - stillPoint.Y;
                if (dx * dx + dy * dy >= 4)
                {
                    stillPoint = e.Position; stillSince = clock.Now;
                    if (tip) { tip = false; Emit(GestureEffectKind.Tooltip, right); }
                }
            }
            return false;
        }
        if (e.Kind is MouseEventKind.Wheel or MouseEventKind.HorizontalWheel)
        {
            RequestProbe();
            if (settings.Wheel.FocusUnderCursor) Emit(GestureEffectKind.Focus, input: e);
            if (e.Kind == MouseEventKind.Wheel && settings.Wheel.FastScrollOnScrollBar &&
                clock.Now >= wheelDisabledUntil && Fresh(e.Position) && cache!.Value.VerticalScrollBar)
            {
                Emit(GestureEffectKind.Replay, input: e with { Delta = e.Delta * settings.Wheel.FastScrollMultiplier });
                return true;
            }
            return false;
        }
        if (e.Kind != MouseEventKind.Down) return false;
        if (e.Button == PointerButton.Left && buttons[(int)PointerButton.Right] is { } active && active.Phase is Phase.Gesture or Phase.Pending &&
            active.Settings.Gesture.RapidFire && active.Stroke.HasStroke)
        {
            var left = NewCapture(e);
            if (active.Phase == Phase.Pending)
            {
                left.ParentGesture = active.Token; left.RapidCommand = active.Stroke.Command;
                pending.Add(left.Token, left); return true;
            }
            left.Phase = Phase.Consumed;
            if (active.Stroke.Command is { } command)
            {
                Emit(GestureEffectKind.Command, active, text: command);
                // 要確認: 連続実行のコマンドを一度でも発行したら、右解放時の追加実行を抑止する。
                active.RapidExecuted = true;
            }
            return true;
        }
        if (e.Button == PointerButton.Left && e.Modifiers == 0 && settings.MoveDisabledWindows &&
            Fresh(e.Position) && cache!.Value.DisabledWindow != 0)
        {
            var drag = NewCapture(e); drag.Phase = Phase.Drag;
            drag.Window = cache.Value.DisabledWindow; drag.Origin = cache.Value.WindowOrigin;
            return true;
        }
        bool intercept = e.Button switch
        {
            PointerButton.Right => settings.Gesture.Enabled || settings.TitleBar.MinimizeButtonRightClick != "None",
            PointerButton.Middle => settings.TitleBar.MiddleClick != "None",
            _ => e.Modifiers.HasFlag(PointerModifiers.Shift) && settings.TitleBar.ShiftClick != "None" ||
                e.Modifiers.HasFlag(PointerModifiers.Control) && settings.TitleBar.CtrlClick != "None"
        };
        if (!intercept) return false;
        var capture = NewCapture(e); pending.Add(capture.Token, capture);
        if (e.Button == PointerButton.Right)
        {
            stillPoint = e.Position; stillSince = clock.Now; tip = false;
            Emit(GestureEffectKind.TrailBegin, capture, e);
        }
        Emit(GestureEffectKind.HitTest, capture, e);
        return true;
    }
    private Capture NewCapture(PointerEvent e)
    {
        var c = new Capture(++nextToken, e, clock.Now, settings);
        buttons[(int)e.Button] = c; return c;
    }
    public void Resolve(long token, HitResult result)
    {
        Tick();
        if (!pending.Remove(token, out var c)) return; // timed out/reconfigured: late result cannot execute
        c.Window = result.Window;
        if (result.Command is { } command && result.Window != 0)
        {
            c.Phase = Phase.Consumed; Emit(GestureEffectKind.Command, c, text: command);
            ResolveRapid(c, false);
            if (c.Down.Button == PointerButton.Right) Emit(GestureEffectKind.TrailEnd, c);
        }
        else if (c.Down.Button == PointerButton.Right && result.AllowGesture && c.Settings.Gesture.Enabled)
        {
            c.Phase = Phase.Gesture;
            ResolveRapid(c, true);
            if (c.Up.HasValue) FinishGesture(c);
            else if (c.Stroke.HasStroke) Emit(GestureEffectKind.TrailShow, c, valid: c.Stroke.IsValid);
        }
        else Replay(c);
    }
    private void FinishGesture(Capture c)
    {
        if (!c.Stroke.HasStroke) { Replay(c); return; }
        c.Phase = Phase.Consumed;
        if (!c.RapidExecuted && c.Stroke.Command is { } command) Emit(GestureEffectKind.Command, c, text: command);
    }
    private void Replay(Capture c)
    {
        if (c.Phase is Phase.Replaying or Phase.Passed or Phase.Consumed) return;
        pending.Remove(c.Token);
        c.Phase = Phase.Replaying;
        Emit(GestureEffectKind.Replay, c, c.Down);
        if (c.Up is { } up) Emit(GestureEffectKind.Replay, c, up);
        if (c.Down.Button == PointerButton.Right) Emit(GestureEffectKind.TrailEnd, c);
        ResolveRapid(c, false);
    }
    private void ResolveRapid(Capture parent, bool gesture)
    {
        if (parent.Down.Button != PointerButton.Right) return;
        foreach (var child in pending.Values.Where(c => c.ParentGesture == parent.Token).ToArray())
        {
            if (!gesture) { Replay(child); continue; }
            pending.Remove(child.Token); child.Phase = Phase.Consumed;
            if (child.RapidCommand is { } command)
            { Emit(GestureEffectKind.Command, parent, text: command); parent.RapidExecuted = true; }
        }
    }
    // A real up is allowed through only AFTER SendInput(down) completed on the worker.
    public void ReplayCompleted(long token)
    {
        foreach (var c in buttons)
            if (c?.Token == token && c.Phase == Phase.Replaying) c.Phase = Phase.Passed;
    }
    public void WheelSendFailed()
    {
        wheelDisabledUntil = clock.Now + TimeSpan.FromSeconds(60);
        Emit(GestureEffectKind.Warning, text: "ホイール SendInput 失敗: 高速スクロールの横取りを60秒停止");
    }
    public void Tick()
    {
        if (probeActiveUntil.HasValue && clock.Now > probeActiveUntil.Value)
        { probeActiveUntil = null; probePending = false; }
        if (!paused && probeActiveUntil.HasValue && clock.Now >= nextProbe && (probePending || clock.Now >= refreshProbeAt))
        {
            SendProbe();
        }
        // Maximum 3 current buttons plus already released decisions; iteration only when pending.
        if (pending.Count != 0)
        {
            expired.Clear();
            foreach (var c in pending.Values)
                if (clock.Now - c.At >= TimeSpan.FromSeconds(2)) expired.Add(c);
            foreach (var c in expired)
            { Replay(c); Emit(GestureEffectKind.Warning, text: "ボタン判定が2秒未確定: 送り直して解除"); }
        }
        var right = buttons[(int)PointerButton.Right];
        if (right == null || right.Phase is not (Phase.Pending or Phase.Gesture)) return;
        if (!right.Stroke.HasStroke && clock.Now - right.At >= TimeSpan.FromMilliseconds(right.Settings.Gesture.StartTimeoutMs))
        { Replay(right); return; }
        if (right.Phase == Phase.Gesture && right.Stroke.HasStroke && !tip &&
            clock.Now - stillSince >= TimeSpan.FromMilliseconds(right.Settings.Gesture.TooltipDelayMs))
        {
            tip = true; Emit(GestureEffectKind.Tooltip, right, new(MouseEventKind.Move, position), right.Stroke.Description);
        }
    }
    private void RequestProbe()
    {
        probeActiveUntil = clock.Now + TimeSpan.FromSeconds(3);
        if (clock.Now >= nextProbe) SendProbe();
        else probePending = true;
    }
    private void SendProbe()
    {
        probePending = false;
        nextProbe = clock.Now + TimeSpan.FromMilliseconds(30);
        refreshProbeAt = clock.Now + TimeSpan.FromMilliseconds(50);
        Emit(GestureEffectKind.Probe, input: new(MouseEventKind.Move, position));
    }
}

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
