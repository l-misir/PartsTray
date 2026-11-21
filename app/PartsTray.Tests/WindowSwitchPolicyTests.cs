// 窓の候補条件と、前後の切り替え・復元・下へ回す操作を OS に触れず検証します。
// 最小化や別モニターの扱い、重なり順の選び方を変えるときに開きます。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class WindowSwitchPolicyTests
{
    private static WindowSwitchWindow Open(long id, long monitor = 10) => new(id, monitor);
    private static WindowSwitchWindow Minimized(long id, long monitor = 10, uint flags = 2) =>
        new(id, monitor, IsMinimized: true, IsMaximized: false, PlacementFlags: flags);

    [Theory]
    [InlineData(false, 2, 1)]
    [InlineData(true, 3, 0)]
    public void OpenMaximizedWindowsKeepExistingOrder(bool previous, long target, long bottom)
    {
        WindowSwitchWindow[] windows = [Open(1), Open(2), Open(3)];
        Assert.Equal(new WindowSwitchPlan(target, bottom, false),
            WindowSwitchPolicy.Select(windows, 10, 1, previous));
        Assert.Equal(new long[] { 1, 2, 3 }, windows.Select(w => w.Window));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedMinimizedWindowMustBeRestored(bool previous)
    {
        Assert.Equal(new WindowSwitchPlan(2, previous ? 0 : 1, true),
            WindowSwitchPolicy.Select([Open(1), Minimized(2)], 10, 1, previous));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(6, true)]
    public void MinimizedCandidateRequiresRestoreToMaximizedFlag(uint flags, bool expected)
    {
        Assert.Equal(expected, WindowSwitchPolicy.IsCandidate(Minimized(1, flags: flags), 10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsOnAnotherMonitorAreExcluded(bool minimized)
    {
        var foreign = minimized ? Minimized(2, 20) : Open(2, 20);
        Assert.False(WindowSwitchPolicy.IsCandidate(foreign, 10));
        Assert.Equal(new WindowSwitchPlan(3, 1, false),
            WindowSwitchPolicy.Select([Open(1), foreign, Open(3)], 10, 1, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeEligibilityExclusionsAreRespected(bool minimized)
    {
        var excluded = (minimized ? Minimized(2) : Open(2)) with { IsEligible = false };
        Assert.False(WindowSwitchPolicy.IsCandidate(excluded, 10));
        Assert.Equal(default, WindowSwitchPolicy.Select([Open(1), excluded], 10, 1, false));
    }

    [Fact]
    public void NormalOpenWindowsAndNullHandlesAreExcluded()
    {
        Assert.False(WindowSwitchPolicy.IsCandidate(Open(1) with { IsMaximized = false }, 10));
        Assert.False(WindowSwitchPolicy.IsCandidate(Open(0), 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(4)]
    public void NonCandidateForegroundUsesTopOpenCandidate(long foreground)
    {
        WindowSwitchWindow[] windows = [Minimized(1), Open(2), Minimized(3), Open(4, 20)];
        Assert.Equal(new WindowSwitchPlan(3, 2, true),
            WindowSwitchPolicy.Select(windows, 10, foreground, false));
    }

    [Fact]
    public void CandidateForegroundTakesPriorityOverTopOpenWindow()
    {
        Assert.Equal(new WindowSwitchPlan(3, 2, false),
            WindowSwitchPolicy.Select([Open(1), Open(2), Open(3)], 10, 2, false));
    }

    [Fact]
    public void NextWrapsFromBottomToTop()
    {
        Assert.Equal(new WindowSwitchPlan(1, 3, true),
            WindowSwitchPolicy.Select([Minimized(1), Open(2), Open(3)], 10, 3, false));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void AllMinimizedWindowsHaveNoCurrentWindow(bool previous, long target)
    {
        Assert.Equal(new WindowSwitchPlan(target, 0, true),
            WindowSwitchPolicy.Select([Minimized(1), Minimized(2), Minimized(3)], 10, 99, previous));
    }

    [Fact]
    public void MinimizedCurrentIsNeverSentToBottom()
    {
        Assert.Equal(new WindowSwitchPlan(2, 0, false),
            WindowSwitchPolicy.Select([Minimized(1), Open(2)], 10, 1, false));
    }

    [Fact]
    public void PreviousUsesBottomCandidateEvenWhenForegroundIsElsewhere()
    {
        Assert.Equal(new WindowSwitchPlan(3, 0, true),
            WindowSwitchPolicy.Select([Minimized(1), Open(2), Minimized(3)], 10, 99, true));
    }

    [Fact]
    public void PreviousDoesNothingWhenBottomIsAlreadyCurrent()
    {
        Assert.Equal(default, WindowSwitchPolicy.Select([Open(1), Open(2)], 10, 2, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroCandidatesDoNothing(bool previous)
    {
        Assert.Equal(default, WindowSwitchPolicy.Select([], 10, 1, previous));
        Assert.Equal(default, WindowSwitchPolicy.Select([Open(1, 20), Minimized(2, flags: 0)], 10, 1, previous));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 99)]
    [InlineData(true, 99)]
    public void OnlyCurrentCandidateDoesNothing(bool previous, long foreground)
    {
        Assert.Equal(default, WindowSwitchPolicy.Select([Open(1)], 10, foreground, previous));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleMinimizedCandidateIsRestoredWhenThereIsNoCurrentWindow(bool previous)
    {
        // §2 の「今の窓がなければ一番上」を優先。見える窓がないままになるのを防ぐ。
        Assert.Equal(new WindowSwitchPlan(1, 0, true),
            WindowSwitchPolicy.Select([Minimized(1)], 10, 99, previous));
        Assert.Equal(default, WindowSwitchPolicy.Select([Minimized(1)], 10, 1, previous));
    }

    [Fact]
    public void RepeatedNextCyclesThroughAllWindowsInZOrder()
    {
        var windows = new List<WindowSwitchWindow> { Open(1), Minimized(2), Open(3) };
        long foreground = 1;
        foreach (long expected in new long[] { 2, 3, 1, 2 })
        {
            var plan = WindowSwitchPolicy.Select(windows, 10, foreground, false);
            Assert.Equal(expected, plan.Target);
            Assert.Equal(foreground, plan.SendToBottom);
            var bottom = windows.Single(w => w.Window == plan.SendToBottom);
            windows.Remove(bottom); windows.Add(bottom);
            var target = windows.Single(w => w.Window == plan.Target);
            windows.Remove(target);
            windows.Insert(0, target with { IsMinimized = false, IsMaximized = true });
            foreground = plan.Target;
        }
    }
}
