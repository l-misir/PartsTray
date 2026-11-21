// 偽の窓で、カーソル下のスクロールバーの判定とフォーカス移動を確かめます。
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
        public ScrollBarBounds? ClientBounds;
        public int ScrollBarWidth = 17;
        public bool IsEnabled = true, IsVisible = true, Descendant, QuerySuccess = true, AttachSuccess = true, FocusSuccess = true;
        public bool ThrowOnFocus;
        public Action? OnAttach;
        public Func<PixelPoint, long>? ChildAt;
        public Func<long, string>? NameOf;
        public long Foreground { get; set; } = 10;
        public uint CurrentThread => 1;
        public int LastError => 5;
        public readonly List<long> HitTargets = [], FocusTargets = [], BoundsTargets = [];
        public readonly List<uint> HitTimeouts = [];
        public readonly List<PixelPoint> WidthPoints = [];
        public readonly List<(uint From, uint To, bool Attach)> Attachments = [];
        public long WindowAt(PixelPoint point) => ChildAt?.Invoke(point) ?? Child;
        public long Parent(long window) => ParentWindow;
        public long Root(long window) => RootWindow;
        public string ClassName(long window) => NameOf?.Invoke(window) ?? Name;
        public long Style(long window) => WindowStyle;
        public int NonClientHit(long window, PixelPoint point, uint timeoutMilliseconds)
        {
            HitTargets.Add(window); HitTimeouts.Add(timeoutMilliseconds);
            return Hit;
        }
        public bool TryGetClientBounds(long window, out ScrollBarBounds bounds)
        {
            BoundsTargets.Add(window);
            bounds = ClientBounds.GetValueOrDefault();
            return ClientBounds.HasValue;
        }
        public int ScrollBarWidthAt(PixelPoint point) { WidthPoints.Add(point); return ScrollBarWidth; }
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
    [Fact] public void NativeHitUsesOnlyTheRemainingTimeAtEitherPoint()
    {
        var windows = new Windows { Hit = 0 };
        var policy = new PointerWindowPolicy(windows, new FakeLog());
        int remaining = 42;
        int Remaining() => remaining;
        Assert.False(policy.IsVerticalScrollBar(new(1919, 160), Remaining));
        remaining = 7;
        Assert.False(policy.IsVerticalScrollBar(new(1911, 160), Remaining));
        remaining = 0;
        Assert.False(policy.IsVerticalScrollBar(new(1911, 160), Remaining));
        Assert.Equal(new uint[] { 42, 7 }, windows.HitTimeouts);
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
    [InlineData(17, -220, 0, false)]
    [InlineData(17, -219, 0, true)]
    [InlineData(26, -228, 0, true)]
    [InlineData(26, -229, 0, false)]
    [InlineData(17, -201, -60, true)]
    [InlineData(17, -200, 0, false)]
    [InlineData(17, -201, -61, false)]
    [InlineData(17, -201, 100, false)]
    [InlineData(0, -201, 0, false)]
    public void ChromiumUsesTheHitChildsClientBoundsAndTheMetricAtThePointer(int width, int x, int y, bool expected)
    {
        var windows = new Windows
        {
            Name = "Chrome_RenderWidgetHostHWND", WindowStyle = 0,
            ClientBounds = new(-700, -60, -200, 100), ScrollBarWidth = width
        };
        var point = new PixelPoint(x, y);
        var policy = new PointerWindowPolicy(windows, new FakeLog());

        var result = policy.CheckScrollBar(point, null);
        Assert.Equal(expected, result.Hit);
        Assert.Equal(expected ? windows.Child : 0, result.ChromiumWindow);
        Assert.Equal(new[] { windows.Child }, windows.BoundsTargets);
        Assert.Equal(new[] { point }, windows.WidthPoints);
    }

    [Theory]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("Chrome_RenderWidgetHostHWND_extra")]
    [InlineData("SysTreeView32")]
    public void OnlyTheRenderWidgetChecksTheChromiumBand(string className)
    {
        var windows = new Windows { Name = className, WindowStyle = 0, ClientBounds = new(10, 20, 100, 200) };
        var policy = new PointerWindowPolicy(windows, new FakeLog());
        Assert.False(policy.IsVerticalScrollBar(new(99, 50)));
        Assert.Empty(windows.BoundsTargets);
        Assert.Empty(windows.WidthPoints);
    }

    [Theory]
    [InlineData(1919)]
    [InlineData(1918)]
    public void ChromiumBandUsesTheInwardPointWhenTheCursorIsOnTheFrame(int x)
    {
        var cursor = new PixelPoint(x, 500);
        var inside = EdgeWheelPolicy.InwardPoint(cursor, new(0, 0, 1920, 1080), 96);
        var points = new ScrollBarProbe(cursor, inside);
        var windows = new Windows
        {
            WindowStyle = 0, ChildAt = p => p.X >= 1918 ? 11 : 22,
            NameOf = w => w == 11 ? "Chrome_WidgetWin_1" : "Chrome_RenderWidgetHostHWND",
            ClientBounds = new(10, 50, 1918, 1078)
        };
        var policy = new PointerWindowPolicy(windows, new FakeLog());
        var (direct, inward) = points.CheckNative(policy, () => 50);

        Assert.False(direct.Hit);
        Assert.True(inward is { Hit: true, Result: "Chromium", ChromiumWindow: 22 });
        Assert.Equal(22L, Assert.Single(windows.BoundsTargets));
        Assert.Equal(new PixelPoint(1911, 500), Assert.Single(windows.WidthPoints));
    }

    [Fact]
    public void OnlyTheFirstChromiumHitIsLoggedAfterNativeChecks()
    {
        var windows = new Windows { Name = "Chrome_RenderWidgetHostHWND" };
        var log = new FakeLog();
        var policy = new PointerWindowPolicy(windows, log);
        var point = new PixelPoint(99, 50);

        Assert.True(policy.IsVerticalScrollBar(point)); // HTVSCROLL
        Assert.Empty(windows.BoundsTargets);
        windows.Hit = 1;
        Assert.False(policy.IsVerticalScrollBar(point)); // クライアント領域の取得失敗
        Assert.Empty(log.Lines);
        windows.ClientBounds = new(10, 20, 100, 200);
        Assert.True(policy.IsVerticalScrollBar(point));
        windows.Child = 21;
        Assert.True(policy.IsVerticalScrollBar(point));
        Assert.Equal("スクロールバー（Chromium の右端）を見分けました", Assert.Single(log.Lines));
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
