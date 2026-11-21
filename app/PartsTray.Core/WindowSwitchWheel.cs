// ウィンドウをホイールで選ぶ時間・方向・移動量と記録用の集計を管理します。OS には触れません。
// 量のため方や終了条件を変えるときに開きます。テストでは時計を差し替えます。
namespace PartsTray.Core;

public readonly record struct WindowSwitchWheelResult(bool Handled, int Steps);
public readonly record struct WindowSwitchWheelSummary(long VerticalCount, long VerticalDelta,
    long HorizontalCount, long HorizontalDelta, long NextCount, long PreviousCount,
    long DeviceRotationCount = 0, long DeviceRotationTotal = 0)
{
    public override string ToString() => $"ホイールで切り替え：縦 {VerticalCount} 回（合計 {VerticalDelta}）、横 {HorizontalCount} 回（合計 {HorizontalDelta}）、次へ {NextCount}・前へ {PreviousCount}" +
        (DeviceRotationCount == 0 ? "" : $"、HID++ {DeviceRotationCount} 回（生の回転量合計 {DeviceRotationTotal}）");
}

public sealed class WindowSwitchWheel(Func<TimeSpan> now)
{
    private WindowKeySettings settings = new();
    private TimeSpan deadline;
    private bool untilRelease, deviceSeen;
    private readonly ThumbWheelConverter deviceConverter = new();
    private long vertical, horizontal;
    private int verticalDirection, horizontalDirection;
    public bool IsActive { get; private set; }
    public WindowSwitchWheelSummary Summary { get; private set; }

    public bool Start(WindowKeySettings value, bool untilRelease = false)
    {
        value.Validate();
        settings = value;
        this.untilRelease = untilRelease;
        vertical = horizontal = 0;
        verticalDirection = horizontalDirection = 0;
        Summary = default;
        deviceSeen = false;
        deviceConverter.Reset();
        IsActive = untilRelease || value.WheelSwitchMs > 0;
        if (IsActive && !untilRelease) deadline = now() + TimeSpan.FromMilliseconds(value.WheelSwitchMs);
        return IsActive;
    }

    public bool Stop()
    {
        bool wasActive = IsActive;
        IsActive = false;
        vertical = horizontal = 0;
        return wasActive;
    }

    public bool Expire() => IsActive && !untilRelease && now() >= deadline && Stop();

    // 自分が送った入力は時間の延長も終了もしない。外部の injected 入力は普通に扱う。
    public void ButtonDown(nuint extraInfo = 0)
    {
        if (extraInfo != KeyStroke.ExtraInfo) Stop();
    }

    public WindowSwitchWheelResult Receive(bool isHorizontal, int delta, nuint extraInfo = 0)
    {
        if (extraInfo == KeyStroke.ExtraInfo) return default;
        Expire();
        if (!IsActive) return default;
        if (!untilRelease) deadline = now() + TimeSpan.FromMilliseconds(settings.WheelSwitchMs);
        if (isHorizontal && deviceSeen) return new(true, 0);
        return Count(isHorizontal, delta);
    }

    public WindowSwitchWheelResult ReceiveDeviceRotation(short rotation)
    {
        if (!settings.ThumbWheelFromDevice || rotation == 0) return default;
        Expire();
        if (!IsActive) return default;
        if (!untilRelease) deadline = now() + TimeSpan.FromMilliseconds(settings.WheelSwitchMs);
        if (!deviceSeen) { horizontal = 0; horizontalDirection = 0; }
        deviceSeen = true;
        Summary = Summary with { DeviceRotationCount = Summary.DeviceRotationCount + 1,
            DeviceRotationTotal = Summary.DeviceRotationTotal + rotation };
        return Count(true, deviceConverter.Convert(rotation, 100, 1));
    }

    private WindowSwitchWheelResult Count(bool isHorizontal, int delta)
    {
        long directed = isHorizontal ? (settings.ReverseHorizontal ? -(long)delta : delta) : -(long)delta;
        ref long remainder = ref (isHorizontal ? ref horizontal : ref vertical);
        ref int direction = ref (isHorizontal ? ref horizontalDirection : ref verticalDirection);
        int sign = Math.Sign(directed);
        if (sign != 0)
        {
            if (sign != direction) remainder = 0;
            direction = sign;
        }
        remainder += directed;
        int steps = checked((int)(remainder / settings.WheelSwitchStep));
        remainder %= settings.WheelSwitchStep;
        var s = Summary;
        Summary = new(s.VerticalCount + (isHorizontal ? 0 : 1), s.VerticalDelta + (isHorizontal ? 0 : delta),
            s.HorizontalCount + (isHorizontal ? 1 : 0), s.HorizontalDelta + (isHorizontal ? delta : 0),
            s.NextCount + Math.Max(0L, steps), s.PreviousCount + Math.Max(0L, -(long)steps),
            s.DeviceRotationCount, s.DeviceRotationTotal);
        return new(true, steps);
    }
}
