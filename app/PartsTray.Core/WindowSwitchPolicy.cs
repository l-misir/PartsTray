// 重なり順の窓情報から、切り替える窓・下に回す窓・復元の要否を純粋に決めます。
// 最大化・最小化の候補条件や、前面が別モニターにある場合の選び方を変えるときに開きます。
namespace PartsTray.Core;

// Monitor は、最小化中なら復元位置、開いていれば現在位置のモニター。
public readonly record struct WindowSwitchWindow(long Window, long Monitor, bool IsMinimized = false,
    bool IsMaximized = true, uint PlacementFlags = 0, bool IsEligible = true, bool IsElevated = false);

public readonly record struct WindowSwitchPlan(long Target, long SendToBottom, bool Restore);

public static class WindowSwitchPolicy
{
    public const uint RestoreToMaximized = 0x2;

    public static bool IsCandidate(WindowSwitchWindow window, long monitor, bool isAdministrator = false) =>
        window.Window != 0 && window.IsEligible && window.Monitor == monitor &&
        (isAdministrator || !window.IsElevated) &&
        (window.IsMinimized ? (window.PlacementFlags & RestoreToMaximized) != 0 : window.IsMaximized);

    // 入力は EnumWindows の上から下への順序。OS を呼ばず、入力の並びも変更しない。
    public static WindowSwitchPlan Select(IEnumerable<WindowSwitchWindow> windows, long monitor,
        long foreground, bool previous, bool isAdministrator = false)
    {
        var candidates = windows.Where(w => IsCandidate(w, monitor, isAdministrator)).ToArray();
        if (candidates.Length == 0) return default;
        int current = Array.FindIndex(candidates, w => w.Window == foreground);
        if (current < 0) current = Array.FindIndex(candidates, w => !w.IsMinimized);
        if (candidates.Length == 1 && current >= 0) return default;
        int next = previous ? candidates.Length - 1 : current < 0 ? 0 : (current + 1) % candidates.Length;
        if (next == current) return default;
        var target = candidates[next];
        long bottom = !previous && current >= 0 && !candidates[current].IsMinimized
            ? candidates[current].Window : 0;
        return new(target.Window, bottom, target.IsMinimized);
    }
}
