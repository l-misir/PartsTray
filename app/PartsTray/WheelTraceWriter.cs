using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PartsTray.Core;

namespace PartsTray;

internal sealed class WheelTraceWriter(ILog log) : IDisposable
{
    private const int QueueLimit = 20_000, EntryLimit = 200_000;
    private readonly object lifecycle = new();
    private Recording? active, recording;
    private Thread? thread;
    private int written;
    private bool requested, disposed, failureLogged;

    private sealed class Recording : IDisposable
    {
        public readonly ConcurrentQueue<WheelTraceEntry> Queue = new();
        public readonly ManualResetEvent Stop = new(false);
        public readonly DateTime Start = DateTime.Now;
        public readonly long StartTimestamp = Stopwatch.GetTimestamp();
        public int Accepting = 1, Producers, Queued;
        public long Dropped;
        public void Dispose() => Stop.Dispose();
    }

    // フックからは無効時に参照を 1 回読むだけ。有効時もロック・ファイル I/O・待機は行わない。
    public void Enqueue(WheelTraceEntry entry)
    {
        var current = Volatile.Read(ref active);
        if (current == null) return;
        Interlocked.Increment(ref current.Producers);
        try
        {
            if (Volatile.Read(ref current.Accepting) == 0) return;
            if (Interlocked.Increment(ref current.Queued) > QueueLimit)
            {
                Interlocked.Decrement(ref current.Queued);
                Interlocked.Increment(ref current.Dropped);
                return;
            }
            try { current.Queue.Enqueue(entry); }
            catch
            {
                Interlocked.Decrement(ref current.Queued);
                Interlocked.Increment(ref current.Dropped);
            }
        }
        finally { Interlocked.Decrement(ref current.Producers); }
    }

    public void SetEnabled(bool enabled)
    {
        lock (lifecycle)
        {
            if (disposed || requested == enabled) return;
            requested = enabled;
            StopRecording();
            if (!enabled || written >= EntryLimit) return;
            var current = new Recording();
            recording = current;
            Volatile.Write(ref active, current);
            thread = new Thread(() => Consume(current)) { IsBackground = true, Name = "PartsTray wheel trace" };
            thread.Start();
        }
    }

    private void StopRecording()
    {
        if (recording is not { } current) return;
        Volatile.Write(ref current.Accepting, 0);
        Interlocked.CompareExchange(ref active, null, current);
        current.Stop.Set();
        thread?.Join();
        current.Dispose();
        recording = null; thread = null;
    }

    private void Consume(Recording current)
    {
        int sessionWritten = 0;
        long? previous = null;
        bool headerPending = true;
        try
        {
            log.Write($"横スクロール記録: 開始（logs/wheel_{current.Start.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log）");
            do
            {
                // 停止と同時に受け取った分も書く。待つのは背景スレッドだけ。
                if (Volatile.Read(ref current.Accepting) == 0)
                    while (Volatile.Read(ref current.Producers) != 0) Thread.Yield();
                long dropped = Interlocked.Exchange(ref current.Dropped, 0);
                if (headerPending || dropped > 0 || !current.Queue.IsEmpty)
                {
                    string folder = Path.Combine(AppContext.BaseDirectory, "logs");
                    Directory.CreateDirectory(folder);
                    string date = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    using var stream = new FileStream(Path.Combine(folder, $"wheel_{date}.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var output = new StreamWriter(stream, new UTF8Encoding(false));
                    if (headerPending) { output.WriteLine(WheelTrace.Header(current.Start)); headerPending = false; }
                    if (dropped > 0) output.WriteLine(FormattableString.Invariant($"# 混雑: {dropped} 件を捨てた"));
                    for (int i = 0; i < QueueLimit && written < EntryLimit && current.Queue.TryDequeue(out var entry); i++)
                    {
                        Interlocked.Decrement(ref current.Queued);
                        DateTime wallClock = current.Start.AddSeconds((entry.Timestamp - current.StartTimestamp) / (double)Stopwatch.Frequency);
                        double elapsed = previous.HasValue ? (entry.Timestamp - previous.Value) * 1000.0 / Stopwatch.Frequency : 0;
                        output.WriteLine(WheelTrace.Format(entry, wallClock, elapsed));
                        previous = entry.Timestamp;
                        written++; sessionWritten++;
                    }
                }
                if (written >= EntryLimit)
                {
                    // 要確認: 200000 件は設定の再有効化でもリセットせず、アプリ起動全体で制限する。
                    log.Write("横スクロール記録: 上限 200000 件に達したため停止");
                    break;
                }
                if (Volatile.Read(ref current.Accepting) == 0 && current.Queue.IsEmpty) break;
                current.Stop.WaitOne(250);
            } while (true);
        }
        catch (Exception ex)
        {
            // 要確認: 書き込み失敗時はこの記録を停止し、false→true の設定変更で再試行する。入力処理は継続する。
            if (!failureLogged) { failureLogged = true; log.Write($"横スクロール記録: 書き込み失敗: {ex.Message}"); }
        }
        finally
        {
            Volatile.Write(ref current.Accepting, 0);
            Interlocked.CompareExchange(ref active, null, current);
            while (Volatile.Read(ref current.Producers) != 0) Thread.Yield();
            while (current.Queue.TryDequeue(out _)) Interlocked.Decrement(ref current.Queued);
            log.Write($"横スクロール記録: 終了 {sessionWritten} 件");
        }
    }

    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            StopRecording();
        }
    }
}
