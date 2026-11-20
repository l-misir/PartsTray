// AirPods が飛ばしている電波を受け取り、いちばん近い（電波の強い）AirPods の電池の値を知らせます。
// 受け取る条件（電波の強さ・どれくらい前まで使うか）を変えるときはここを編集します。
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using PartsTray.Core;

namespace PartsTray;

// Windows は AirPods の電池の値を持っていない（11/20 実機：AirPods 4 の部品のどれにも電池の値がなかった）。
// AirPods は近くの iPhone 向けに、左右とケースの残量を電波で飛ばし続けているので、それを受け取る（AirPodsDesktop と同じ方法）。
// この電波は周りの人の AirPods からも届く。あなたの AirPods がこの PC につながっている間だけ受け取り（BatteryModule が入り切り）、
// 電波の強さが MinRssi 以上のもののうち、ここ Window 秒でいちばん強いものを選ぶ。人の多い場所では、まれに他人のものを拾うことがある。
internal sealed class AirPodsWatcher : IDisposable
{
    private const short MinRssi = -75;                               // これより弱い（遠い）電波は使わない
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(15); // これより前に聞こえた機器は候補から外す
    private readonly ILog log;
    private readonly object gate = new();
    private readonly Dictionary<ulong, (AirPodsReading Reading, double Rssi, DateTime At)> heard = [];
    private BluetoothLEAdvertisementWatcher? watcher;
    private AirPodsReading? published;
    private bool disposed;

    // 選んだ AirPods の値が変わったときに、受信のスレッドから呼ぶ。
    public event Action<AirPodsReading>? Changed;

    public AirPodsWatcher(ILog log) => this.log = log;

    public void SetRunning(bool run)
    {
        lock (gate)
        {
            if (disposed || run == (watcher != null)) return;
            if (run) Start(); else Stop();
        }
    }

    private void Start()
    {
        try
        {
            var w = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            w.Received += Received;
            w.Stopped += (_, e) => { if (e.Error != Windows.Devices.Bluetooth.BluetoothError.Success) log.Write($"AirPods の電波の受信が止まりました: {e.Error}"); };
            w.Start();
            watcher = w;
            log.Write("AirPods の電波の受信を始めました");
        }
        catch (Exception ex) { log.Write($"AirPods の電波の受信を始められません: {ex.Message}"); watcher = null; }
    }

    private void Stop()
    {
        try { watcher?.Stop(); } catch (Exception ex) { log.Write($"AirPods の電波の受信を止められません: {ex.Message}"); }
        if (watcher != null) watcher.Received -= Received;
        watcher = null; heard.Clear(); published = null;
        log.Write("AirPods の電波の受信を止めました");
    }

    private void Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        short rssi = args.RawSignalStrengthInDBm;
        if (rssi < MinRssi) return;
        foreach (var data in args.Advertisement.ManufacturerData)
        {
            if (data.CompanyId != AirPods.AppleCompanyId) continue;
            var bytes = new byte[data.Data.Length];
            using (var reader = DataReader.FromBuffer(data.Data)) reader.ReadBytes(bytes);
            if (AirPods.Parse(bytes) is not { } reading) continue;
            AirPodsReading? changed = null;
            lock (gate)
            {
                if (disposed || watcher == null) return;
                var now = DateTime.UtcNow;
                // 電波の強さは揺れるので、同じ機器（アドレス）の前の値とならして比べる。
                double smooth = heard.TryGetValue(args.BluetoothAddress, out var old) && now - old.At < Window ? old.Rssi * 0.7 + rssi * 0.3 : rssi;
                heard[args.BluetoothAddress] = (reading, smooth, now);
                foreach (var key in heard.Where(kv => now - kv.Value.At >= Window).Select(kv => kv.Key).ToArray()) heard.Remove(key);
                var best = heard.Values.MaxBy(v => v.Rssi).Reading;
                if (best != published) { published = best; changed = best; }
            }
            if (changed != null) Changed?.Invoke(changed);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (watcher != null) Stop();
            disposed = true;
        }
    }
}
