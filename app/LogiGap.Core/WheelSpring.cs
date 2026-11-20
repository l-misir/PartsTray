namespace LogiGap.Core;

/// <summary>Clock-driven critically damped wheel motion; owns no timers or threads.</summary>
public sealed class WheelSpringSmoother : IWheelSmoother
{
    private double springSeconds = 0.05;
    private double target, position, velocity;
    private long emitted;
    private TimeSpan interval = TimeSpan.FromMilliseconds(8), last, lastAdd, nextDue;
    private bool moving;
    public TimeSpan? NextDue => moving ? nextDue : null;

    public void Configure(int springMs, int intervalMs)
    {
        if (springMs is < 10 or > 500) throw new ArgumentOutOfRangeException(nameof(springMs));
        if (intervalMs is < 4 or > 50) throw new ArgumentOutOfRangeException(nameof(intervalMs));
        springSeconds = springMs / 1000.0;
        interval = TimeSpan.FromMilliseconds(intervalMs);
        Reset();
    }

    public void Add(int amount, TimeSpan now)
    {
        if (amount == 0) return;
        if (!moving)
        {
            target = position = velocity = 0;
            emitted = 0;
            last = nextDue = now;
            moving = true;
        }
        else if (amount > 0 && target < emitted || amount < 0 && target > emitted)
        {
            target = position = emitted;
            velocity = 0;
            nextDue = now;
        }
        target += amount;
        lastAdd = now;
    }

    public int Take(TimeSpan now)
    {
        if (!moving || now < nextDue) return 0;
        double dt = Math.Clamp((now - last).TotalSeconds, 0.001, 0.05);
        last = now;
        double omega = 2 / springSeconds;
        double x = omega * dt;
        double e = 1 / (1 + x + 0.48 * x * x + 0.235 * x * x * x);
        double change = position - target;
        double temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * e;
        position = target + (change + temp) * e;
        if (change < 0 && position > target || change > 0 && position < target)
        {
            position = target;
            velocity = 0;
        }
        // 要確認: §5.2(7) の 500ms 後も継続する例と矛盾するため、§3.3 の 10×T で全量を送り切る終了条件を優先する。
        if (Math.Abs(target - position) < 0.5 && Math.Abs(velocity) < 20 ||
            now - lastAdd >= TimeSpan.FromSeconds(10 * springSeconds))
        {
            position = target;
            velocity = 0;
            moving = false;
        }
        long whole = moving ? (long)Math.Truncate(position) : (long)target;
        int amount = checked((int)(whole - emitted));
        emitted = whole;
        if (moving)
        {
            nextDue += interval;
            if (nextDue <= now) nextDue = now + interval;
        }
        return amount;
    }

    public void Reset()
    {
        target = position = velocity = 0;
        emitted = 0;
        last = lastAdd = nextDue = default;
        moving = false;
    }
}
