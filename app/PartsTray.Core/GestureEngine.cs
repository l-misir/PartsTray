// マウス入力からジェスチャー・クリック・ホイールの取り込みを判定します。
// 入力の優先順位や押下から解放までの状態遷移を変えるときに開きます。
namespace PartsTray.Core;

/// <summary>Single owner (hook message thread). No native calls, waits, drawing, or external callbacks
/// except nonblocking posts. Worker replies return to that same owner by token.</summary>
public sealed partial class GestureEngine(IClock clock, IGestureEffects effects, AppSettings settings)
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
        public WindowSwitchWheel? RightWheel;
        public bool WheelReported;
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
    private bool reportedEdgeRedirect;
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
            c.RightWheel?.Stop();
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
        foreach (var c in buttons) if (c != null) ReportRightWheel(c);
        Array.Clear(buttons); pending.Clear(); expired.Clear();
        cache = null; tip = false; probePending = false; probeActiveUntil = null;
        Emit(GestureEffectKind.TrailEnd);
    }
    private void DiscardMissedRelease(Capture c)
    {
        ReportRightWheel(c);
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
        foreach (var capture in buttons) if (capture != null) ReportRightWheel(capture);
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
    private bool FreshWheel(PixelPoint point) => Fresh(point) && effects.IsCurrentWheelWindow(cache!.Value.WheelWindow);
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
            ReportRightWheel(c);
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
            if (TakeRightWheel(e)) return true;
            RequestProbe();
            if (settings.Wheel.FocusUnderCursor) Emit(GestureEffectKind.Focus, input: e);
            if (!FreshWheel(e.Position))
            {
                if (e.Kind != MouseEventKind.Wheel || !settings.Wheel.FastScrollOnScrollBar ||
                    !settings.Wheel.EdgeAssist || clock.Now < wheelDisabledUntil) return false;
                var firstTarget = effects.FirstChromiumEdge(e.Position, settings.Wheel);
                return firstTarget.Window != 0 && RedirectWheel(e, firstTarget);
            }
            var hit = cache!.Value;
            if (hit.WheelExcluded) return false;
            if (e.Kind == MouseEventKind.Wheel && settings.Wheel.FastScrollOnScrollBar &&
                clock.Now >= wheelDisabledUntil && (hit.VerticalScrollBar || settings.Wheel.EdgeAssist && hit.EdgeScrollBar))
            {
                var target = settings.Wheel.EdgeAssist ? hit.ChromiumEdge : default;
                int delta = e.Delta * settings.Wheel.FastScrollMultiplier;
                // WM_MOUSEWHEEL の回転量は符号付き16ビット。大きな倍率でも向きを反転させない。
                if (target.Window != 0) delta = Math.Clamp(delta, short.MinValue, short.MaxValue);
                effects.Post(new(GestureEffectKind.FastScroll, Input: e with { Delta = delta }, At: clock.Now,
                    Target: target, WheelWindow: hit.WheelWindow, OriginalDelta: e.Delta));
                return true;
            }
            if (!settings.Wheel.EdgeRedirect || hit.Redirect.Window == 0) return false;
            return RedirectWheel(e, hit.Redirect);
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
    private bool RedirectWheel(PointerEvent input, EdgeWheelTarget target)
    {
        bool posted = effects.TryRedirectWheel(input, target);
        bool first = posted && !reportedEdgeRedirect;
        reportedEdgeRedirect |= posted;
        if (first || settings.Diagnostics.ScrollProbe)
        {
            // 投函済みの入力は、記録の失敗によって二重に届けない。
            try { effects.Post(new(GestureEffectKind.EdgeRedirectReport, Input: input, Target: target,
                Steps: posted ? 1 : 0, Valid: first)); } catch { }
        }
        return posted;
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
}
