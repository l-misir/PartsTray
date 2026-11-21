// 他プロセスへの問い合わせを専用スレッドで 1 件だけ実行し、必要に応じて呼び出し元の待機時間を制限します。
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PartsTray.Core;

public enum WorkerWaitStatus { Completed, Timeout, Busy, Faulted }
public readonly record struct WorkerWaitResult<T>(WorkerWaitStatus Status, T Result, TimeSpan Elapsed);

/// <summary>A dedicated background thread with at most one accepted job (queued or running).
/// Requests while busy are dropped rather than accumulated.</summary>
public sealed class SingleFlightWorker : IDisposable
{
    private readonly BlockingCollection<Action> queue = new(1);
    private readonly Thread thread;
    private int busy, disposed;
    public SingleFlightWorker(string name, Action<Exception>? failed = null)
    {
        thread = new Thread(() =>
        {
            foreach (var work in queue.GetConsumingEnumerable())
            {
                try { if (Volatile.Read(ref disposed) == 0) work(); }
                catch (Exception ex) { failed?.Invoke(ex); }
                finally { Volatile.Write(ref busy, 0); }
            }
        }) { Name = name, IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    public bool TryPost(Action work)
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        try { if (queue.TryAdd(work)) return true; }
        catch (InvalidOperationException) { }
        Volatile.Write(ref busy, 0); return false;
    }
    public T RunWithTimeout<T>(Func<Func<bool>, T> operation, int timeoutMilliseconds, T fallback, long? startedAt = null)
    {
        long start = startedAt ?? Stopwatch.GetTimestamp();
        bool InTime() => Stopwatch.GetElapsedTime(start).TotalMilliseconds < timeoutMilliseconds;
        var wait = Wait(() => InTime() ? operation(InTime) : fallback, timeoutMilliseconds, start);
        return wait.Status == WorkerWaitStatus.Completed ? wait.Result : fallback;
    }

    // 待つ側だけに期限を設ける。遅れて完了した問い合わせも、同じ専用スレッドで結果を記憶できる。
    public WorkerWaitResult<T> RunForCache<T>(Func<Func<bool>, T> operation, int timeoutMilliseconds)
        => Wait(() => operation(() => Volatile.Read(ref disposed) == 0), timeoutMilliseconds, Stopwatch.GetTimestamp());

    private WorkerWaitResult<T> Wait<T>(Func<T> operation, int timeoutMilliseconds, long start)
    {
        var budget = TimeSpan.FromMilliseconds(timeoutMilliseconds);
        WorkerWaitResult<T> End(WorkerWaitStatus status, T result = default!)
            => new(status, result, Stopwatch.GetElapsedTime(start));
        if (Stopwatch.GetElapsedTime(start) >= budget) return End(WorkerWaitStatus.Timeout);
        var completion = new TaskCompletionSource<WorkerWaitResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(() =>
        {
            try { completion.SetResult(End(WorkerWaitStatus.Completed, operation())); }
            catch { completion.SetResult(End(WorkerWaitStatus.Faulted)); }
        })) return End(WorkerWaitStatus.Busy);

        var remaining = budget - Stopwatch.GetElapsedTime(start);
        if (remaining <= TimeSpan.Zero || !completion.Task.Wait(remaining) || Stopwatch.GetElapsedTime(start) >= budget)
            return End(WorkerWaitStatus.Timeout);
        return completion.Task.Result;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) queue.CompleteAdding();
        // Do not join: a foreign GUI thread may be hung inside SetFocus/AttachThreadInput.
    }
}
