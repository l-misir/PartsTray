// 判定スレッドで窓のプロセス名と投函可否を覚え、ホイールを受けた時は保存した情報を読みます。
using System.Collections.Concurrent;
using PartsTray.Core;
using N = PartsTray.GestureNative;

namespace PartsTray;

internal sealed partial class WindowsGestureServices
{
    private sealed record WindowProcess(uint Id, nint Window, string Name, bool CanPost, long CheckedAt);
    private readonly ConcurrentDictionary<uint, WindowProcess> windowProcesses = new();
    private readonly bool administrator = ProcessElevation.IsAdministrator;

    private static uint ProcessId(nint root)
    {
        N.GetWindowThreadProcessId(root, out uint pid);
        return pid;
    }

    // PID の再利用に備え、調査した窓がまだ同じプロセスか確かめる。
    private WindowProcess? CachedProcess(uint pid) =>
        windowProcesses.TryGetValue(pid, out var process) && ProcessId(process.Window) == pid ? process : null;

    // この書き込みとプロセス照会は判定スレッドのみ。同じアプリの子窓は名前・権限を共有する。
    private WindowProcess? RememberProcess(nint window)
    {
        uint pid = ProcessId(window);
        if (pid == 0) return null;
        long now = Environment.TickCount64;
        if (CachedProcess(pid) is { } saved &&
            (saved.Name.Length != 0 || now - saved.CheckedAt < 1000)) return saved;
        string name = Path.GetFileName(Executable(window));
        bool canPost = pid != Environment.ProcessId && (administrator || !ProcessElevation.IsWindowElevated(window));
        if (ProcessId(window) != pid) return null;
        return windowProcesses[pid] = new(pid, window, name, canPost, now);
    }

    public void PrepareWheelWindows(bool rebuildRenderers)
    {
        var wheel = readSettings().Wheel;
        bool prepareEdges = wheel.FastScrollOnScrollBar && wheel.EdgeAssist;
        if (prepareEdges)
        {
            PrepareChromiumMonitors();
            if (rebuildRenderers) scannedRoots.Clear();
        }
        N.EnumWindows((root, _) =>
        {
            if (N.IsWindowVisible(root) && RememberProcess(root) is { } process && prepareEdges &&
                wheel.AllowsFastScroll(process.Name))
                PrepareChromiumRoot(root, process.Id);
            return true;
        }, 0);
        ForgetClosedChromiumWindows();
        foreach (var (pid, process) in windowProcesses)
            if (ProcessId(process.Window) != pid) windowProcesses.TryRemove(pid, out _);
    }

    // 初回の等倍投函に使う。フックではプロセスを開かず、HWND と PID の一致も確かめる。
    private bool ChromiumWheelAllowed(nint root, WheelSettings wheel) =>
        CachedProcess(ProcessId(root)) is { CanPost: true } process && wheel.AllowsFastScroll(process.Name);

    public static bool IsCurrentWheelWindow(WheelWindow window)
    {
        if (window.Root == 0 || window.ProcessId == 0) return false;
        nint source = N.WindowFromPoint(new(window.Point.X, window.Point.Y));
        if (source == 0 || source != window.Source) return false;
        nint root = N.GetAncestor(source, 2);
        return root != 0 && root == window.Root && ProcessId(root) == window.ProcessId;
    }

    public (PointerEvent Input, EdgeWheelTarget Target) PrepareFastScroll(GestureEffect effect)
    {
        nint root = 0;
        if (N.GetCursorPos(out var current))
        {
            var point = new PixelPoint(current.X, current.Y);
            // 受信時にモニター内へ補正した点は、実カーソルが動いていなければ同じ補正を使う。
            root = Root(point == effect.Input.Position ? effect.WheelWindow.Point : point);
        }
        uint pid = root != 0 ? ProcessId(root) : 0;
        string? name = CachedProcess(pid)?.Name;
        bool targetCurrent = effect.Target.Window == 0 || IsCurrentRenderer(effect);
        return effect.FastWheelDelivery(readSettings().Wheel, root, pid, name, targetCurrent);
    }

    private bool IsCurrentRenderer(GestureEffect effect)
    {
        nint target = (nint)effect.Target.Window;
        nint inside = N.WindowFromPoint(new(effect.Target.Position.X, effect.Target.Position.Y));
        return inside != 0 && N.GetAncestor(inside, 2) == effect.WheelWindow.Root &&
            (target == inside || target == effect.WheelWindow.Source && IsCurrentWheelWindow(effect.WheelWindow)) &&
            ProcessId(target) == effect.Target.ProcessId && rendererWindows.ClassName(target) == "Chrome_RenderWidgetHostHWND";
    }
}
