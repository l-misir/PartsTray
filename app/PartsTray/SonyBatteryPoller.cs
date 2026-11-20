// Sony のヘッドホンに、数分に一度だけ短くつないで、電池と充電の状態を聞きます（やり取りの形は PartsTray.Core/SonyMdr.cs）。
// 聞く間隔や待つ時間を変えるときはここを編集します。
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using PartsTray.Core;

namespace PartsTray;

// Windows は Bluetooth 機器の「残量」しか持たず、充電中かどうかが分からない。Sony のヘッドホンは、スマホの Sony のアプリと同じ
// 通り道（RFCOMM）で聞けば、残量と充電の状態を教えてくれる（11/20 実機：WH-1000XM6 で、ケーブルあり・満タンが 3、なしが 0）。
// この通り道は一度に 1 つのアプリしか使えないので、つなぎっぱなしにせず、Every ごとに数秒だけつないで聞いたらすぐ切る。
// 11/20：最初は 2 分ごとにしたが、ユーザー：「不便。せめて 20 秒、10 秒、なんなら 5 秒」で、既定 20 秒・設定で 5〜600 秒にした。
// スマホのアプリが使っているなどで聞けないときは何もしない（Windows の残量と、残量の増え方からの推測のまま）。
internal sealed class SonyBatteryPoller : IDisposable
{
    public TimeSpan Every { get; set; } = TimeSpan.FromSeconds(20);      // 同じ機器に聞く間隔（設定 battery.sonyPollSeconds）
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);   // 聞いた値を使う長さ（過ぎたら Windows の値に戻る）
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(8);   // 1 回のやり取りで待つ長さ
    private readonly ILog log;
    private readonly Action<Action> post;
    private readonly Dictionary<string, (DateTime At, BatteryReading Reading)> latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> tried = new(StringComparer.Ordinal);
    private readonly HashSet<string> notSony = new(StringComparer.Ordinal), busy = new(StringComparer.Ordinal);
    private volatile bool disposed;

    public event Action? Updated;   // 新しい値が取れたとき（UI スレッド）

    public SonyBatteryPoller(ILog log, Action<Action> post) { this.log = log; this.post = post; }

    // UI スレッドから呼ぶ。つながっている候補のうち、前に聞いてから Every たったものに聞きに行く。
    // 切れた機器は覚えていたことを忘れる（次につながった直後に聞き直す）。Sony の通り道がない機器には、つながっている間はもう聞かない。
    public void Consider(IEnumerable<BatteryDevice> candidates)
    {
        var now = DateTime.UtcNow;
        foreach (var d in candidates)
        {
            // 切れたら覚えていたことを忘れる。Sony の通り道が見つからなかった機器も、つながり直したらもう一度確かめる
            // （つないだ直後は、通り道の一覧が一時的に空のことがあるため。一度で「Sony ではない」と決めつけない）。
            if (!d.Connected) { latest.Remove(d.Id); tried.Remove(d.Id); notSony.Remove(d.Id); continue; }
            if (disposed || notSony.Contains(d.Id) || busy.Contains(d.Id)) continue;
            if (tried.TryGetValue(d.Id, out var at) && now - at < Every) continue;
            if (!BluetoothBatteryReader.TryGetAddress(d.Id, out ulong address)) { notSony.Add(d.Id); continue; }
            tried[d.Id] = now; busy.Add(d.Id);
            string id = d.Id, name = d.Name;
            _ = Task.Run(async () =>
            {
                var (reading, sony) = await ReadAsync(address);
                post(() => Done(id, name, reading, sony));
            });
        }
    }

    // 聞いて取れた新しい値（Fresh のあいだ）。なければ null。
    public BatteryReading? Latest(string id) => latest.TryGetValue(id, out var v) && DateTime.UtcNow - v.At < Fresh ? v.Reading : null;

    private void Done(string id, string name, BatteryReading? reading, bool sony)
    {
        busy.Remove(id);
        if (disposed) return;
        if (!sony) { notSony.Add(id); return; }
        if (reading == null) return;
        bool first = !latest.ContainsKey(id);
        latest[id] = (DateTime.UtcNow, reading);
        if (first) log.Write($"{name} の電池を Sony の方式で直接読めました（{reading.Percent}%・{reading.Charge}）");
        Updated?.Invoke();
    }

    // (値, Sony の通り道があったか)。通り道がなければ Sony ではないとみなす。つながらない・待ちきれないときは、また後で聞く。
    private static async Task<(BatteryReading? Reading, bool Sony)> ReadAsync(ulong address)
    {
        using var cts = new CancellationTokenSource(Limit);
        try
        {
            using var device = await BluetoothDevice.FromBluetoothAddressAsync(address).AsTask(cts.Token);
            if (device is null || device.ConnectionStatus != BluetoothConnectionStatus.Connected) return (null, true);
            foreach (var (uuid, v2) in new[] { (SonyMdr.ServiceV2, true), (SonyMdr.ServiceV1, false) })
            {
                var id = RfcommServiceId.FromUuid(uuid);
                var found = await device.GetRfcommServicesForIdAsync(id, BluetoothCacheMode.Cached).AsTask(cts.Token);
                if (found.Services.Count == 0) found = await device.GetRfcommServicesForIdAsync(id, BluetoothCacheMode.Uncached).AsTask(cts.Token);
                if (found.Services.Count == 0) continue;
                return (await QueryAsync(found.Services[0], v2, cts.Token), true);
            }
            return (null, false);
        }
        catch (Exception) { return (null, true); }
    }

    // 初期化 → 電池を聞く → 返事を受け取ったら切る。ヘッドホンからのメッセージにはすべて ACK を返す（中身は使わない）。
    private static async Task<BatteryReading?> QueryAsync(RfcommDeviceService service, bool v2, CancellationToken ct)
    {
        using var socket = new StreamSocket();
        await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(ct);
        using var writer = new DataWriter(socket.OutputStream);
        using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        async Task Send(byte[] frame) { writer.WriteBytes(frame); await writer.StoreAsync().AsTask(ct); }
        var buffer = new List<byte>();
        byte hostSeq = 0; bool acked = false, ready = false, asked = false;
        await Send(SonyMdr.InitRequest());
        while (true)
        {
            uint n = await reader.LoadAsync(1024).AsTask(ct);
            if (n == 0) return null;
            var chunk = new byte[n]; reader.ReadBytes(chunk); buffer.AddRange(chunk);
            foreach (var m in SonyMdr.Decode(buffer))
            {
                if (m.Type == SonyMdr.Ack) { hostSeq = m.Seq; acked = true; }
                else
                {
                    await Send(SonyMdr.AckFor(m));
                    if (SonyMdr.Battery(m.Payload) is { } reading) return reading;
                    if (SonyMdr.IsInitReply(m, out bool isV2)) { ready = true; v2 = isV2; }
                }
                // 初期化の返事と、こちらの初期化への ACK の両方がそろったら、電池を聞く（番号は最後の ACK の番号）。
                if (ready && acked && !asked) { asked = true; await Send(SonyMdr.BatteryRequest(v2, hostSeq)); }
            }
        }
    }

    public void Dispose() => disposed = true;
}
