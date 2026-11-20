using System.Text.Json;

namespace PartsTray.Core;

public sealed record HiddenWindow(long Handle, int ProcessId, long ProcessStartedUtcTicks, string Title);
public sealed class HiddenWindowStore(string path)
{
    public IReadOnlyList<HiddenWindow> Load() => !File.Exists(path) ? [] :
        JsonSerializer.Deserialize<HiddenWindow[]>(File.ReadAllText(path), AppSettings.JsonOptions)
        ?? throw new InvalidDataException("隠したウィンドウ一覧が空です");
    public void Save(IEnumerable<HiddenWindow> windows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, windows, AppSettings.JsonOptions);
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
}
