// フックの入力判定、再送、設定とジェスチャーの動作を実機なしで検証します。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class GestureTests
{
    private sealed class FakeHits : IGestureHitTest
    {
        public HitResult Result = new(42, true);
        public HitResult HitTest(PointerEvent input, AppSettings settings) => Result;
        public PointerCache Probe(PixelPoint point, TimeSpan at) => new(point, at, true);
    }
    private sealed class FakeMouse : IGestureInput
    {
        public readonly List<PointerEvent> Sent = [];
        public bool Success = true;
        public bool Replay(PointerEvent input) { Sent.Add(input with { ExtraInfo = KeyStroke.ExtraInfo }); return Success; }
    }
    private sealed class H : IGestureEffects
    {
        public readonly FakeClock Clock = new();
        public readonly FakeHits Hits = new();
        public readonly FakeMouse Input = new();
        public readonly List<GestureEffect> Effects = [];
        public readonly GestureEngine Engine;
        private int consumed;
        public H(AppSettings? settings = null) => Engine = new(Clock, this, GestureTestSettings.AllOn(settings));
        public void Post(GestureEffect effect) => Effects.Add(effect);
        public bool Event(MouseEventKind kind, int x = 0, int y = 0, PointerButton button = PointerButton.Right, PointerModifiers mods = 0, int delta = 0)
            => Engine.Handle(new(kind, new(x, y), button, delta, mods));
        public long Down(PointerButton button = PointerButton.Right, PointerModifiers mods = 0)
        {
            Assert.True(Event(MouseEventKind.Down, button: button, mods: mods));
            return Effects.Last(e => e.Kind == GestureEffectKind.HitTest).Token;
        }
        public void Decide(long token) => Engine.Resolve(token, Hits.HitTest(default, new()));
        public void Start() => Decide(Down());
        public void Advance(int ms) { Clock.Now += TimeSpan.FromMilliseconds(ms); Engine.Tick(); }
        public GestureEffect[] Replays => Effects.Where(e => e.Kind == GestureEffectKind.Replay).ToArray();
        public string?[] Commands => Effects.Where(e => e.Kind == GestureEffectKind.Command).Select(e => e.Text).ToArray();
        public void SendQueued()
        {
            while (consumed < Effects.Count)
            {
                var e = Effects[consumed++];
                if (e.Kind == GestureEffectKind.FastScroll)
                {
                    if (!Input.Replay(e.Input)) Engine.WheelSendFailed();
                }
                else if (e.Kind == GestureEffectKind.Replay)
                {
                    Input.Replay(e.Input);
                    if (e.Input.Kind == MouseEventKind.Down) Engine.ReplayCompleted(e.Token);
                }
            }
        }
    }

    [Fact] public void StrokeThresholdAxesAndRepeatedDirection()
    {
        var s = new StrokeRecognizer(new(0, 0), new());
        s.Move(new(9, 0)); Assert.False(s.HasStroke);
        s.Move(new(12, 0)); Assert.Equal("R", s.Pattern);
        s.Move(new(24, 0)); s.Move(new(40, 0)); Assert.Equal("R", s.Pattern);
        s = new(new(0, 0), new()); s.Move(new(0, 12)); s.Move(new(12, 12));
        Assert.Equal("DR", s.Pattern); Assert.Equal("Close", s.Command);
    }
    [Fact] public void ValidityRequiresExactMatchAndEntireTrailChangesColor()
    {
        var h = new H(); h.Start();
        h.Event(MouseEventKind.Move, 0, 12);
        Assert.False(h.Effects.Last(e => e.Kind == GestureEffectKind.TrailShow).Valid);
        h.Event(MouseEventKind.Move, 12, 12);
        Assert.True(h.Effects.Last(e => e.Kind == GestureEffectKind.TrailShow).Valid);
        h.Event(MouseEventKind.Move, 12, 0);
        Assert.False(h.Effects.Last(e => e.Kind == GestureEffectKind.TrailShow).Valid);
        Assert.True(h.Event(MouseEventKind.Up, 12, 0)); Assert.Empty(h.Commands); Assert.Empty(h.Replays);
    }
    [Fact] public void StationaryClickReplaysDownBeforeUp()
    {
        var h = new H(); h.Start(); Assert.True(h.Event(MouseEventKind.Up)); h.SendQueued();
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
        Assert.All(h.Input.Sent, e => { Assert.Equal(PointerButton.Right, e.Button); Assert.Equal(KeyStroke.ExtraInfo, e.ExtraInfo); });
    }
    [Fact] public void TimeoutReplaysDownThenAllowsPhysicalUpOnlyAfterSendCompleted()
    {
        var h = new H(); h.Start(); h.Advance(999); Assert.Empty(h.Replays);
        h.Advance(1); Assert.Single(h.Replays); h.SendQueued();
        Assert.False(h.Event(MouseEventKind.Move, 30, 0)); Assert.False(h.Event(MouseEventKind.Up));
        Assert.Single(h.Input.Sent); Assert.Empty(h.Commands);
    }
    [Fact] public void UpWhileTimeoutReplayIsQueuedIsAlsoReplayedAfterDown()
    {
        var h = new H(); h.Start(); h.Advance(1000);
        Assert.True(h.Event(MouseEventKind.Up)); h.SendQueued();
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
    }
    [Theory]
    [InlineData(-12, 0, "Back")]
    [InlineData(12, 0, "Forward")]
    public void GestureConsumesRightPairAndExecutesExactlyOnce(int x, int y, string command)
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, x, y);
        Assert.True(h.Event(MouseEventKind.Up, x, y)); Assert.Equal(new[] { command }, h.Commands);
        Assert.Empty(h.Replays); Assert.False(h.Event(MouseEventKind.Up)); Assert.Single(h.Commands);
    }
    [Fact] public void RapidFireThreeClicksDoesNotFireOnRightRelease()
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, -12);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(h.Event(MouseEventKind.Down, -12, button: PointerButton.Left));
            Assert.True(h.Event(MouseEventKind.Up, -12, button: PointerButton.Left));
        }
        h.Event(MouseEventKind.Up, -12); Assert.Equal(new[] { "Back", "Back", "Back" }, h.Commands); Assert.Empty(h.Replays);
    }
    [Fact] public void RapidFireLeftReleaseStillConsumedAfterRightRelease()
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, -12);
        Assert.True(h.Event(MouseEventKind.Down, button: PointerButton.Left));
        h.Event(MouseEventKind.Up); Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Left));
        Assert.Single(h.Commands);
    }
    [Fact] public void RapidClicksWhileHitTestPendingAreExecutedBeforeFinalRelease()
    {
        var h = new H(); long id = h.Down(); h.Event(MouseEventKind.Move, -12);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(h.Event(MouseEventKind.Down, button: PointerButton.Left));
            Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Left));
        }
        h.Event(MouseEventKind.Up); h.Decide(id);
        Assert.Equal(new[] { "Back", "Back", "Back" }, h.Commands); Assert.Empty(h.Replays);
    }
    [Fact] public void PendingRapidClickIsReturnedIfGestureStartIsRejected()
    {
        var h = new H(); long id = h.Down(); h.Event(MouseEventKind.Move, -12);
        h.Event(MouseEventKind.Down, button: PointerButton.Left); h.Event(MouseEventKind.Up, button: PointerButton.Left);
        h.Hits.Result = new(42, false); h.Decide(id); h.SendQueued();
        Assert.Equal(new[] { PointerButton.Right, PointerButton.Left, PointerButton.Left }, h.Input.Sent.Select(e => e.Button));
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
        Assert.False(h.Event(MouseEventKind.Up)); Assert.Empty(h.Commands);
    }
    [Fact] public void ShutdownBalancesAnAlreadyReplayedHeldButton()
    {
        var h = new H(); h.Start(); h.Advance(1000); h.SendQueued(); h.Engine.Shutdown(); h.SendQueued();
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
        Assert.True(h.Event(MouseEventKind.Up));
    }
    [Fact] public void RapidFireInvalidSequenceConsumesBothLeftEdgesWithoutCommand()
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, 0, 12);
        Assert.True(h.Event(MouseEventKind.Down, button: PointerButton.Left));
        Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Left));
        h.Event(MouseEventKind.Up); Assert.Empty(h.Commands); Assert.Empty(h.Replays);
    }
    [Fact] public void DisablingRapidFireLeavesLeftButtonAlone()
    {
        var h = new H(new() { Gesture = new() { RapidFire = false } }); h.Start(); h.Event(MouseEventKind.Move, -12);
        Assert.False(h.Event(MouseEventKind.Down, button: PointerButton.Left));
        Assert.False(h.Event(MouseEventKind.Up, button: PointerButton.Left));
        h.Event(MouseEventKind.Up); Assert.Single(h.Commands);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonCaptionMiddleReplayKeepsOrderEvenIfUpArrivedBeforeDecision(bool earlyUp)
    {
        var h = new H(); var id = h.Down(PointerButton.Middle); h.Hits.Result = new(42, false);
        if (earlyUp) Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Middle));
        h.Decide(id);
        if (!earlyUp) Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Middle));
        h.SendQueued(); Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
    }
    [Fact] public void TitleHitConsumesBothEdgesAndPostsActionToOriginalWindow()
    {
        var h = new H(); var id = h.Down(PointerButton.Middle);
        h.Event(MouseEventKind.Up, button: PointerButton.Middle);
        h.Hits.Result = new(99, false, "CloseWindow"); h.Decide(id);
        Assert.Equal(new[] { "CloseWindow" }, h.Commands); Assert.Empty(h.Replays);
        Assert.Equal(99, h.Effects.Single(e => e.Kind == GestureEffectKind.Command).Window);
    }
    [Fact] public void OrdinaryLeftClickIsNeverHeldForHitTesting()
    {
        var h = new H(); Assert.False(h.Event(MouseEventKind.Down, button: PointerButton.Left)); Assert.Empty(h.Effects);
    }
    [Theory]
    [InlineData(PointerModifiers.Shift)]
    [InlineData(PointerModifiers.Control)]
    public void ModifiedLeftClickIsDeferred(PointerModifiers modifier)
    {
        var h = new H(); h.Down(PointerButton.Left, modifier); Assert.Single(h.Effects, e => e.Kind == GestureEffectKind.HitTest);
    }
    [Fact] public void TwoSecondSafetyReplaysAndIgnoresLateSuccessfulHit()
    {
        var h = new H(); long id = h.Down(PointerButton.Middle);
        h.Event(MouseEventKind.Up, button: PointerButton.Middle);
        h.Advance(1999); Assert.Empty(h.Replays); h.Advance(1);
        h.Hits.Result = new(42, false, "CloseWindow"); h.Decide(id); h.SendQueued();
        Assert.Empty(h.Commands); Assert.Equal(2, h.Input.Sent.Count);
    }
    [Fact] public void ResolveChecksDeadlineEvenWithoutTimerTick()
    {
        var h = new H(); long id = h.Down(PointerButton.Middle);
        h.Clock.Now = TimeSpan.FromSeconds(2); h.Hits.Result = new(42, false, "CloseWindow"); h.Decide(id);
        Assert.Empty(h.Commands); Assert.Single(h.Replays);
    }
    [Fact] public void PendingRightStrokeUsesTwoSecondSafetyAndCannotExecuteLate()
    {
        var h = new H(); long id = h.Down(); h.Event(MouseEventKind.Move, -12); h.Advance(2000);
        h.Decide(id); h.SendQueued(); Assert.False(h.Event(MouseEventKind.Up)); Assert.Empty(h.Commands);
    }
    [Fact] public void SelectedExplorerOrExcludedProcessReturnsRightDownImmediately()
    {
        var h = new H(); long id = h.Down(); h.Hits.Result = new(42, false); h.Decide(id); h.SendQueued();
        Assert.Single(h.Input.Sent); Assert.False(h.Event(MouseEventKind.Move, 25));
        Assert.False(h.Event(MouseEventKind.Up)); Assert.Empty(h.Commands);
    }
    [Fact] public void QuickGestureCompletedWhileHitTestPendingIsRetained()
    {
        var h = new H(); long id = h.Down(); h.Event(MouseEventKind.Move, -12); h.Event(MouseEventKind.Up, -12);
        h.Decide(id); Assert.Equal(new[] { "Back" }, h.Commands); Assert.Empty(h.Replays);
    }
    [Fact] public void SuccessivePendingClicksDoNotOverwriteEarlierRelease()
    {
        var h = new H(); long a = h.Down(PointerButton.Middle); h.Event(MouseEventKind.Up, button: PointerButton.Middle);
        long b = h.Down(PointerButton.Middle); h.Event(MouseEventKind.Up, button: PointerButton.Middle);
        h.Hits.Result = new(42, false); h.Decide(a); h.Decide(b); h.SendQueued();
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up, MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
    }
    [Theory]
    [InlineData(100, 0, true)]
    [InlineData(101, 0, false)]
    [InlineData(0, 1, false)]
    public void WheelOnlyInterceptsFreshCacheAtExactPosition(int age, int x, bool intercept)
    {
        var h = new H(); h.Engine.SetCache(h.Hits.Probe(new(0, 0), h.Clock.Now)); h.Advance(age);
        Assert.Equal(intercept, h.Event(MouseEventKind.Wheel, x, delta: 120)); h.SendQueued();
        if (intercept) Assert.Equal(600, Assert.Single(h.Input.Sent).Delta);
        else Assert.Empty(h.Input.Sent);
    }
    [Fact] public void MissingAndNegativeCacheNeverDelayWheelAndHorizontalAlwaysPasses()
    {
        var h = new H(); Assert.False(h.Event(MouseEventKind.Wheel, delta: -120));
        h.Engine.SetCache(new(new(0, 0), h.Clock.Now, false)); Assert.False(h.Event(MouseEventKind.Wheel, delta: 120));
        h.Engine.SetCache(new(new(0, 0), h.Clock.Now, true)); Assert.False(h.Event(MouseEventKind.HorizontalWheel, delta: 120));
        Assert.DoesNotContain(h.Effects, e => e.Kind is GestureEffectKind.FastScroll or GestureEffectKind.Replay);
    }
    [Fact] public void WheelFailureStopsInterceptionForSixtySeconds()
    {
        var h = new H(); h.Engine.SetCache(new(new(0, 0), h.Clock.Now, true)); h.Input.Success = false;
        Assert.True(h.Event(MouseEventKind.Wheel, delta: -120)); h.SendQueued(); Assert.Equal(-600, Assert.Single(h.Input.Sent).Delta);
        h.Advance(59999); h.Engine.SetCache(new(new(0, 0), h.Clock.Now, true)); Assert.False(h.Event(MouseEventKind.Wheel, delta: 120));
        h.Advance(1); Assert.True(h.Event(MouseEventKind.Wheel, delta: 120));
    }
    [Fact] public void ProbeIsThrottledToThirtyMilliseconds()
    {
        var h = new H();
        for (int i = 0; i < 100; i++) { h.Event(MouseEventKind.Move, i); h.Advance(1); }
        Assert.Equal(4, h.Effects.Count(e => e.Kind == GestureEffectKind.Probe));
    }
    [Fact] public void LastMoveWithinThrottleIsProbedEvenWhenCursorStops()
    {
        var h = new H(); h.Event(MouseEventKind.Move); h.Advance(5); h.Event(MouseEventKind.Move, 15);
        h.Advance(25); Assert.Equal(new PixelPoint(15, 0), h.Effects.Last(e => e.Kind == GestureEffectKind.Probe).Input.Position);
    }
    [Theory]
    [InlineData(MouseEventKind.Down)]
    [InlineData(MouseEventKind.Up)]
    [InlineData(MouseEventKind.Move)]
    [InlineData(MouseEventKind.Wheel)]
    [InlineData(MouseEventKind.HorizontalWheel)]
    public void OwnInputNeverEntersAnyDecisionIncludingTimeouts(MouseEventKind kind)
    {
        var h = new H(); h.Down(); int count = h.Effects.Count; h.Clock.Now = TimeSpan.FromSeconds(3);
        Assert.False(h.Engine.Handle(new(kind, new(20, 0), PointerButton.Right, 120, PointerModifiers.Shift, KeyStroke.ExtraInfo)));
        Assert.Equal(count, h.Effects.Count);
    }
    [Fact] public void PausedFeaturesPassAllNewInputAndReturnPendingButtons()
    {
        var h = new H(); long id = h.Down(PointerButton.Middle); h.Engine.SetPaused(true); h.SendQueued();
        Assert.False(h.Event(MouseEventKind.Up, button: PointerButton.Middle));
        h.Hits.Result = new(42, false, "CloseWindow"); h.Decide(id); Assert.Empty(h.Commands);
        int count = h.Effects.Count;
        foreach (var kind in Enum.GetValues<MouseEventKind>()) Assert.False(h.Event(kind, delta: 120));
        Assert.Equal(count, h.Effects.Count);
    }
    [Fact] public void PausingRecognizedGestureDoesNotInventContextMenu()
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, -12); h.Engine.SetPaused(true);
        Assert.True(h.Event(MouseEventKind.Up)); Assert.Empty(h.Commands); Assert.Empty(h.Replays);
    }
    [Fact] public void SettingsReloadInvalidatesPendingResultAndUsesNewBindings()
    {
        var h = new H(); long id = h.Down(PointerButton.Middle);
        h.Engine.UpdateSettings(GestureTestSettings.AllOn(new() { Gesture = new() { Gestures = new() { ["L"] = "Refresh" } } }));
        h.Hits.Result = new(42, false, "CloseWindow"); h.Decide(id); Assert.Empty(h.Commands);
        h.SendQueued(); h.Event(MouseEventKind.Up, button: PointerButton.Middle);
        h.Hits.Result = new(42, true); h.Start(); h.Event(MouseEventKind.Move, -12); h.Event(MouseEventKind.Up);
        Assert.Equal(new[] { "Refresh" }, h.Commands);
    }
    [Fact] public void TrailIncludesPressAndSubThresholdPointsBeforeBecomingVisible()
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, 3, 1); h.Event(MouseEventKind.Move, 9, 2);
        Assert.DoesNotContain(h.Effects, e => e.Kind == GestureEffectKind.TrailShow);
        h.Event(MouseEventKind.Move, 12, 2);
        Assert.Equal(new[] { new PixelPoint(0, 0), new(3, 1), new(9, 2), new(12, 2) },
            h.Effects.Where(e => e.Kind is GestureEffectKind.TrailBegin or GestureEffectKind.TrailPoint).Select(e => e.Input.Position));
        h.Event(MouseEventKind.Up); Assert.Equal(GestureEffectKind.TrailEnd, h.Effects[^2].Kind);
    }
    [Fact] public void TooltipAppearsAfterStillnessAndDisappearsAtTwoPixels()
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, -12); h.Advance(999);
        Assert.DoesNotContain(h.Effects, e => e.Kind == GestureEffectKind.Tooltip);
        h.Advance(1); Assert.Equal("← 戻る", h.Effects.Last(e => e.Kind == GestureEffectKind.Tooltip).Text);
        h.Event(MouseEventKind.Move, -11); Assert.Equal("← 戻る", h.Effects.Last(e => e.Kind == GestureEffectKind.Tooltip).Text);
        h.Event(MouseEventKind.Move, -10); Assert.Null(h.Effects.Last(e => e.Kind == GestureEffectKind.Tooltip).Text);
    }
    [Fact] public void LongDoodleRetainsAllDirectionsWithoutBecomingValidAgain()
    {
        var s = new StrokeRecognizer(new(-2000, -1000), new());
        for (int i = 0; i < 20000; i++) s.Move(new(-2000 + (i % 2 == 0 ? 12 : 0), -1000));
        Assert.Equal(20000, s.Pattern.Length); Assert.False(s.IsValid); Assert.EndsWith("（未登録）", s.Description);
    }
    [Fact] public void DisabledWindowDragUsesCachedOriginAndSuppressesRelease()
    {
        var h = new H(); h.Engine.SetCache(new(new(0, 0), h.Clock.Now, false, 77, new(-300, 50)));
        Assert.True(h.Event(MouseEventKind.Down, button: PointerButton.Left));
        Assert.False(h.Event(MouseEventKind.Move, 20, -5));
        Assert.False(h.Event(MouseEventKind.Move, 45, -15));
        Assert.False(h.Event(MouseEventKind.Move, 70, 10));
        var moves = h.Effects.Where(e => e.Kind == GestureEffectKind.MoveWindow).ToArray();
        Assert.All(moves, e => Assert.Equal(77, e.Window));
        Assert.Equal(new[] { new PixelPoint(-280, 45), new(-255, 35), new(-230, 60) }, moves.Select(e => e.Input.Position));
        Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Left)); Assert.Empty(h.Replays);
    }
    [Fact] public void StaleDisabledWindowCacheNeverInterceptsOrdinaryClick()
    {
        var h = new H(); h.Engine.SetCache(new(new(0, 0), h.Clock.Now, false, 77)); h.Advance(101);
        Assert.False(h.Event(MouseEventKind.Down, button: PointerButton.Left));
    }
    [Fact] public void GestureSettingsRoundTripWithoutChangingGapSettingsOrExtensionData()
    {
        var original = new AppSettings { Modules = new() { ["future"] = JsonSerializer.SerializeToElement(new { answer = 42 }) } };
        var json = JsonSerializer.Serialize(original, AppSettings.JsonOptions);
        var copy = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!; copy.Validate();
        Assert.Equal("Back", copy.Gesture.Gestures["L"]); Assert.Equal(5, copy.Wheel.FastScrollMultiplier);
        Assert.Equal(2200, copy.GapFiller.Dpi.Primary); Assert.Equal(42, copy.Modules!["future"].GetProperty("answer").GetInt32());
        JsonSerializer.Deserialize<AppSettings>("{\"gapFiller\":{}}", AppSettings.JsonOptions)!.Validate();
    }
    [Fact] public void BadGestureSettingsAreRejectedBeforeApplying()
    {
        Assert.Throws<InvalidDataException>(() => new AppSettings { Gesture = new() { Trail = new() { ValidColor = "red" } } }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { Gesture = new() { Gestures = new() { ["LL"] = "Close" } } }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { Gesture = new() { StartTimeoutMs = 3000 } }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { Wheel = new() { FastScrollMultiplier = 0 } }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { TitleBar = new() { MiddleClick = "Unknown" } }.Validate());
    }
    [Fact] public void HiddenWindowJournalRoundTripsReplacesAndKeepsCorruptionVisible()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "test-state-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "hidden_windows.json"); var store = new HiddenWindowStore(path);
        try
        {
            Assert.Empty(store.Load());
            HiddenWindow[] expected = [new(0x123456789, 123, 456, "日本語のタイトル"), new(88, 999, 1000, "二つ目")];
            store.Save(expected); Assert.Equal(expected, store.Load()); Assert.False(File.Exists(path + ".tmp"));
            store.Save(expected.Take(1)); Assert.Single(store.Load()); store.Save([]); Assert.Empty(store.Load());
            File.WriteAllText(path, "broken"); Assert.Throws<JsonException>(() => store.Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); if (Directory.Exists(folder)) Directory.Delete(folder); }
    }
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1500)]
    public void MissedRightReleaseDiscardsOldDownBeforeEvenCheckingTimeout(bool decided, int delay)
    {
        var h = new H(); long old = h.Down(); if (decided) h.Decide(old);
        h.Clock.Now = TimeSpan.FromMilliseconds(delay); // No Tick: next Down is the first observed event.
        long current = h.Down(); h.Decide(old); h.Decide(current); h.Event(MouseEventKind.Up); h.SendQueued();
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
        Assert.All(h.Replays, e => Assert.Equal(current, e.Token)); Assert.Empty(h.Commands);
        Assert.Contains(h.Effects, e => e.Kind == GestureEffectKind.Warning && e.Token == old);
    }
    [Fact] public void MissedGestureReleaseCannotExecuteOnNextUnmovedClick()
    {
        var h = new H(); h.Start(); h.Event(MouseEventKind.Move, -12);
        h.Decide(h.Down()); h.Event(MouseEventKind.Up); h.SendQueued();
        Assert.Empty(h.Commands); Assert.Equal(2, h.Input.Sent.Count);
    }
    [Fact] public void MissedReleaseDuringReplayBalancesOldDownBeforeNewDown()
    {
        var h = new H(); h.Hits.Result = new(42, false);
        long old = h.Down(PointerButton.Middle); h.Decide(old);
        long current = h.Down(PointerButton.Middle);
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up }, h.Replays.Select(e => e.Input.Kind));
        Assert.All(h.Replays, e => Assert.Equal(old, e.Token));
        h.SendQueued(); // Old completion must not mark the new pending button as Passed.
        Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Middle)); h.Decide(current); h.SendQueued();
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up, MouseEventKind.Down, MouseEventKind.Up }, h.Input.Sent.Select(e => e.Kind));
        Assert.Equal(new[] { old, old, current, current }, h.Replays.Select(e => e.Token));
    }
    [Fact] public void MissedPassedReleaseDoesNotSynthesizeAnotherOldRelease()
    {
        var h = new H(); h.Hits.Result = new(42, false);
        long old = h.Down(PointerButton.Middle); h.Decide(old); h.SendQueued();
        long current = h.Down(PointerButton.Middle); h.Decide(current); h.Event(MouseEventKind.Up, button: PointerButton.Middle);
        Assert.Single(h.Replays, e => e.Token == old);
        Assert.Equal(new[] { MouseEventKind.Down, MouseEventKind.Up }, h.Replays.Where(e => e.Token == current).Select(e => e.Input.Kind));
    }
    [Fact] public void HookLostDropsPassedButtonWithoutReplayingAndPassesItsRelease()
    {
        var h = new H(); h.Start(); h.Advance(1000); h.SendQueued();
        int before = h.Replays.Length; h.Engine.HookLost();
        Assert.False(h.Event(MouseEventKind.Up)); Assert.Equal(before, h.Replays.Length); Assert.Empty(h.Commands);
        Assert.Equal(GestureEffectKind.TrailEnd, h.Effects[^1].Kind);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HookLostDropsPendingOrRecognizedGestureAndIgnoresLateDecision(bool decided)
    {
        var h = new H(); long token = h.Down(); if (decided) h.Decide(token);
        h.Event(MouseEventKind.Move, -12); h.Engine.HookLost(); h.Decide(token); h.Advance(3000);
        Assert.False(h.Event(MouseEventKind.Up)); Assert.Empty(h.Commands); Assert.Empty(h.Replays);
        h.Start(); h.Event(MouseEventKind.Up); Assert.Equal(2, h.Replays.Length);
    }
    [Fact] public void HookLostDoesNotBalanceReplayingButtonOrRetainItsCompletion()
    {
        var h = new H(); h.Hits.Result = new(42, false); h.Decide(h.Down(PointerButton.Middle));
        h.Engine.HookLost(); h.SendQueued();
        Assert.Single(h.Replays); Assert.False(h.Event(MouseEventKind.Up, button: PointerButton.Middle));
    }
    [Fact] public void MissedRightReleaseAlsoInvalidatesDependentRapidClick()
    {
        var h = new H(); long old = h.Down(); h.Event(MouseEventKind.Move, -12);
        Assert.True(h.Event(MouseEventKind.Down, button: PointerButton.Left));
        long current = h.Down(); h.Decide(old); h.Decide(current);
        Assert.True(h.Event(MouseEventKind.Up, button: PointerButton.Left));
        h.Event(MouseEventKind.Up); Assert.Empty(h.Commands); Assert.All(h.Replays, e => Assert.Equal(current, e.Token));
    }
    [Fact] public void StationaryCursorProbesEveryFiftyMillisecondsForOneSecond()
    {
        var h = new H(); h.Event(MouseEventKind.Move, 50, 70);
        for (int i = 0; i < 1000; i++) h.Advance(1);
        var probes = h.Effects.Where(e => e.Kind == GestureEffectKind.Probe).ToArray();
        Assert.Equal(Enumerable.Range(0, 21).Select(i => TimeSpan.FromMilliseconds(i * 50)), probes.Select(e => e.At));
        Assert.All(probes, e => Assert.Equal(new PixelPoint(50, 70), e.Input.Position));
    }
    [Fact] public void StationaryRefreshStopsAfterThreeSecondsAndWheelRestartsIt()
    {
        var h = new H(); h.Event(MouseEventKind.Move);
        for (int i = 0; i < 60; i++) h.Advance(50);
        int count = h.Effects.Count(e => e.Kind == GestureEffectKind.Probe);
        h.Advance(1); h.Advance(1000); Assert.Equal(count, h.Effects.Count(e => e.Kind == GestureEffectKind.Probe));
        h.Engine.SetCache(new(new(0, 0), TimeSpan.FromSeconds(3), true));
        Assert.False(h.Event(MouseEventKind.Wheel, delta: 120));
        var probe = h.Effects.Last(e => e.Kind == GestureEffectKind.Probe);
        Assert.Equal(h.Clock.Now, probe.At); Assert.Equal(count + 1, h.Effects.Count(e => e.Kind == GestureEffectKind.Probe));
        h.Engine.SetCache(h.Hits.Probe(probe.Input.Position, probe.At)); h.Advance(50);
        Assert.True(h.Event(MouseEventKind.Wheel, delta: 120));
    }
    [Fact] public void WheelAndMovementShareThirtyMillisecondProbeLimit()
    {
        var h = new H(); h.Event(MouseEventKind.Move);
        for (int i = 0; i < 150; i++)
        { h.Advance(1); h.Event(i % 2 == 0 ? MouseEventKind.Wheel : MouseEventKind.Move, i, delta: 120); }
        var probes = h.Effects.Where(e => e.Kind == GestureEffectKind.Probe).ToArray();
        Assert.True(probes.Length > 1);
        Assert.All(probes.Zip(probes.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= TimeSpan.FromMilliseconds(30)));
    }
    [Fact] public void PausingOrLosingHookStopsStationaryRefresh()
    {
        var h = new H(); h.Event(MouseEventKind.Move); h.Engine.SetPaused(true); h.Advance(50);
        Assert.Single(h.Effects, e => e.Kind == GestureEffectKind.Probe);
        h.Engine.SetPaused(false); h.Advance(50); Assert.Single(h.Effects, e => e.Kind == GestureEffectKind.Probe);
        h.Event(MouseEventKind.Move); h.Engine.HookLost(); h.Advance(50);
        Assert.Equal(2, h.Effects.Count(e => e.Kind == GestureEffectKind.Probe));
    }
}
