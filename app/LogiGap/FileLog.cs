using System.Collections.Concurrent;
using System.Text;
using LogiGap.Core;

namespace LogiGap;

internal sealed class FileLog : ILog, IDisposable
{
    private readonly BlockingCollection<string> queue = new(4096);
    private readonly Task writer;
    private readonly string folder;
    private int dropped;
    public FileLog(string folder)
    {
        this.folder = folder;
        writer = Task.Run(Consume);
    }
    public void Write(string message)
    {
        try
        {
            if (!queue.TryAdd($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}")) Interlocked.Increment(ref dropped);
        }
        catch (InvalidOperationException) { }
    }
    private void Consume()
    {
        DateOnly lastPrune = default;
        foreach (var line in queue.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(folder);
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (today != lastPrune)
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "app_*.log"))
                        if (DateOnly.TryParseExact(Path.GetFileName(file).AsSpan(4, 8), "yyyyMMdd", out var date) && date < today.AddDays(-6)) File.Delete(file);
                    lastPrune = today;
                }
                int lost = Interlocked.Exchange(ref dropped, 0);
                string text = (lost > 0 ? $"{DateTime.Now:O} ログ混雑: {lost} 行省略{Environment.NewLine}" : "") + line + Environment.NewLine;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                for (int part = 0; ; part++)
                {
                    string path = Path.Combine(folder, $"app_{today:yyyyMMdd}{(part == 0 ? "" : $"_{part}")}.log");
                    if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > 5 * 1024 * 1024) continue;
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    stream.Write(bytes); break;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }
    public void Dispose() { queue.CompleteAdding(); writer.Wait(TimeSpan.FromSeconds(2)); }
}
