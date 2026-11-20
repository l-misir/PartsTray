using System.Collections.Concurrent;

namespace LogiGap.Core;

/// <summary>A dedicated background thread with at most one accepted job (queued or running).
/// Producers never wait; requests while busy are dropped rather than accumulated.</summary>
public sealed class SingleFlightWorker : IDisposable
{
    private readonly BlockingCollection<Action> queue = new(1);
    private readonly Thread thread;
    private int busy, disposed;
    public SingleFlightWorker(string name, Action<Exception> failed)
    {
        thread = new Thread(() =>
        {
            foreach (var work in queue.GetConsumingEnumerable())
            {
                try { if (Volatile.Read(ref disposed) == 0) work(); }
                catch (Exception ex) { failed(ex); }
                finally { Volatile.Write(ref busy, 0); }
            }
        }) { Name = name, IsBackground = true };
        thread.Start();
    }
    public bool TryPost(Action work)
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        try { if (queue.TryAdd(work)) return true; }
        catch (InvalidOperationException) { }
        Volatile.Write(ref busy, 0); return false;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) queue.CompleteAdding();
        // Do not join: a foreign GUI thread may be hung inside SetFocus/AttachThreadInput.
    }
}
