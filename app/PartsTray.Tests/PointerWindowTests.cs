using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class PointerWindowTests
{
    private sealed class Windows : IPointerWindows
    {
        public long Child = 20, RootWindow = 10, ParentWindow = 30, Focused;
        public string Name = "SysTreeView32";
        public long WindowStyle = 0x00200000;
        public int Hit = 7;
        public bool IsEnabled = true, IsVisible = true, Descendant, QuerySuccess = true, AttachSuccess = true, FocusSuccess = true;
        public bool ThrowOnFocus;
        public Action? OnAttach;
        public long Foreground { get; set; } = 10;
        public uint CurrentThread => 1;
        public int LastError => 5;
        public readonly List<long> HitTargets = [], FocusTargets = [];
        public readonly List<(uint From, uint To, bool Attach)> Attachments = [];
        public long WindowAt(PixelPoint point) => Child;
        public long Parent(long window) => ParentWindow;
        public long Root(long window) => RootWindow;
        public string ClassName(long window) => Name;
        public long Style(long window) => WindowStyle;
        public int NonClientHit(long window, PixelPoint point) { HitTargets.Add(window); return Hit; }
        public uint ThreadOf(long window) => 2;
        public bool TryFocusOf(uint thread, out long focus) { Assert.Equal(2u, thread); focus = Focused; return QuerySuccess; }
        public bool IsChild(long parent, long child) => Descendant;
        public bool Enabled(long window) => IsEnabled;
        public bool Visible(long window) => IsVisible;
        public bool Attach(uint from, uint to, bool attach)
        { Attachments.Add((from, to, attach)); if (attach) OnAttach?.Invoke(); return AttachSuccess; }
        public bool Focus(long window)
        {
            FocusTargets.Add(window);
            if (ThrowOnFocus) throw new InvalidOperationException("fake failure");
            return FocusSuccess;
        }
    }
    [Fact] public void ScrollBarHitTestTargetsChildNotRoot()
    {
        var windows = new Windows(); var policy = new PointerWindowPolicy(windows, new FakeLog());
        Assert.True(policy.IsVerticalScrollBar(new(1, 2))); Assert.Equal(new long[] { 20 }, windows.HitTargets);
        windows.Hit = 1; Assert.False(policy.IsVerticalScrollBar(new(1, 2)));
    }
    [Fact] public void RootWindowWithNativeBarIsAlsoSupported()
    {
        var windows = new Windows { Child = 10 }; var policy = new PointerWindowPolicy(windows, new FakeLog());
        Assert.True(policy.IsVerticalScrollBar(default)); Assert.Equal(new long[] { 10 }, windows.HitTargets);
    }
    [Theory]
    [InlineData("Chrome_WidgetWin_1", 0)]
    [InlineData("Microsoft.UI.Content.DesktopChildSiteBridge", 0)]
    [InlineData("ScrollBar", 0)]
    public void WindowsWithoutVerticalStyleNeverReceiveNonClientMessages(string name, long style)
    {
        var windows = new Windows { Name = name, WindowStyle = style }; var policy = new PointerWindowPolicy(windows, new FakeLog());
        Assert.False(policy.IsVerticalScrollBar(default)); Assert.Empty(windows.HitTargets);
    }
    [Fact] public void VerticalScrollBarControlNeedsNoNonClientMessage()
    {
        var windows = new Windows { Name = "ScrollBar", WindowStyle = 1 }; var policy = new PointerWindowPolicy(windows, new FakeLog());
        Assert.True(policy.IsVerticalScrollBar(default)); Assert.Empty(windows.HitTargets);
    }
    [Theory]
    [InlineData("normal", 20)]
    [InlineData("ScrollBar", 30)]
    [InlineData("SysHeader32", 30)]
    public void FocusUsesHwndAndParentsForScrollBarsAndHeaders(string name, long target)
    {
        var windows = new Windows { Name = name }; var policy = new PointerWindowPolicy(windows, new FakeLog());
        policy.FocusUnderCursor(default, () => true);
        Assert.Equal(new[] { target }, windows.FocusTargets);
        Assert.Equal(new[] { (1u, 2u, true), (1u, 2u, false) }, windows.Attachments);
    }
    [Theory]
    [InlineData("background")]
    [InlineData("root")]
    [InlineData("already-focused")]
    [InlineData("descendant-focused")]
    [InlineData("disabled")]
    [InlineData("invisible")]
    [InlineData("no-window")]
    [InlineData("stopped")]
    public void FocusSkipsIneligibleOrAlreadyFocusedWindows(string scenario)
    {
        var windows = new Windows();
        switch (scenario)
        {
            case "background": windows.Foreground = 99; break;
            case "root": windows.Child = 10; break;
            case "already-focused": windows.Focused = 20; break;
            case "descendant-focused": windows.Focused = 21; windows.Descendant = true; break;
            case "disabled": windows.IsEnabled = false; break;
            case "invisible": windows.IsVisible = false; break;
            case "no-window": windows.Child = 0; break;
        }
        new PointerWindowPolicy(windows, new FakeLog()).FocusUnderCursor(default, () => scenario != "stopped");
        Assert.Empty(windows.Attachments); Assert.Empty(windows.FocusTargets);
    }
    [Fact] public void FailedGuiThreadQueryDoesNotAttachOrFocus()
    {
        var windows = new Windows { QuerySuccess = false }; var log = new FakeLog();
        new PointerWindowPolicy(windows, log).FocusUnderCursor(default, () => true);
        Assert.Empty(windows.Attachments); Assert.Empty(windows.FocusTargets); Assert.Single(log.Lines);
    }
    [Fact] public void FailedAttachDoesNotFocusAndSuppressesRepeatedFailureLog()
    {
        var windows = new Windows { AttachSuccess = false }; var log = new FakeLog(); var policy = new PointerWindowPolicy(windows, log);
        policy.FocusUnderCursor(default, () => true); policy.FocusUnderCursor(default, () => true);
        Assert.Empty(windows.FocusTargets); Assert.Single(log.Lines);
        Assert.All(windows.Attachments, a => Assert.True(a.Attach));
    }
    [Fact] public void FailedFocusAlwaysDetachesAndSuccessResetsDuplicateLogSuppression()
    {
        var windows = new Windows { FocusSuccess = false }; var log = new FakeLog(); var policy = new PointerWindowPolicy(windows, log);
        policy.FocusUnderCursor(default, () => true); policy.FocusUnderCursor(default, () => true);
        Assert.Single(log.Lines); Assert.Equal(2, windows.Attachments.Count(a => !a.Attach));
        windows.FocusSuccess = true; policy.FocusUnderCursor(default, () => true);
        windows.FocusSuccess = false; policy.FocusUnderCursor(default, () => true); Assert.Equal(2, log.Lines.Count);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedForegroundOrPauseAfterAttachPreventsFocusButStillDetaches(bool pause)
    {
        var windows = new Windows(); bool allowed = true;
        windows.OnAttach = () => { if (pause) allowed = false; else windows.Foreground = 99; };
        new PointerWindowPolicy(windows, new FakeLog()).FocusUnderCursor(default, () => allowed);
        Assert.Empty(windows.FocusTargets); Assert.Equal(new[] { (1u, 2u, true), (1u, 2u, false) }, windows.Attachments);
    }
    [Fact] public void UnexpectedFocusExceptionStillDetaches()
    {
        var windows = new Windows { ThrowOnFocus = true }; var policy = new PointerWindowPolicy(windows, new FakeLog());
        Assert.Throws<InvalidOperationException>(() => policy.FocusUnderCursor(default, () => true));
        Assert.Equal(new[] { (1u, 2u, true), (1u, 2u, false) }, windows.Attachments);
    }
    [Fact] public async Task BusyFocusWorkerDropsRequestsWithoutBlockingOtherDecisions()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new SingleFlightWorker("fake focus", _ => { });
        try
        {
            Assert.True(worker.TryPost(() => { entered.Set(); release.Wait(); finished.TrySetResult(true); }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            for (int i = 0; i < 100; i++) Assert.False(worker.TryPost(() => throw new InvalidOperationException("must be dropped")));
            // No native APIs: an unrelated fake hit test can complete while focus remains blocked.
            var effects = new Effects(); var engine = new GestureEngine(new FakeClock(), effects, GestureTestSettings.AllOn());
            Assert.True(engine.Handle(new(MouseEventKind.Down, default, PointerButton.Middle)));
            long token = effects.Items.Single(e => e.Kind == GestureEffectKind.HitTest).Token;
            engine.Resolve(token, new(42, false, "CloseWindow"));
            Assert.Single(effects.Items, e => e.Kind == GestureEffectKind.Command);
            worker.Dispose(); Assert.False(worker.TryPost(() => { }));
        }
        finally { release.Set(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
    }
    private sealed class Effects : IGestureEffects
    {
        public List<GestureEffect> Items { get; } = [];
        public void Post(GestureEffect effect) => Items.Add(effect);
    }
}
