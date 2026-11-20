// ペアリング済みの Bluetooth 機器が「つながった・切れた」ことを Windows から受け取って知らせます。
// 知らせを受けたら BatteryModule がすぐに読み直す（60 秒ごとの見回りを待たずに表示するため）。
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using PartsTray.Core;

namespace PartsTray;

// 11/20 ユーザー：「表示が出るの遅くない？ マウスやキーボードはレシーバーがあるから？ AirPods も全然出なかった」。
// マウス・キーボードは受信機がつながった瞬間に知らせてくるが、Bluetooth の機器は 60 秒ごとに見に行くだけだったので、
// つないでから最大 1 分（AirPods はさらに電波を待つ分）遅れていた。Windows の機器の見張り（DeviceWatcher）で、
// 「つながっているか」（System.Devices.Aep.IsConnected）が変わったときに知らせる。
internal sealed class BluetoothConnectionWatcher : IDisposable
{
    private const string IsConnected = "System.Devices.Aep.IsConnected";
    private readonly List<DeviceWatcher> watchers = [];
    private readonly ILog log;
    public event Action? Changed;   // 見張りのスレッドから呼ぶ

    public BluetoothConnectionWatcher(ILog log)
    {
        this.log = log;
        // 普通の Bluetooth（ヘッドホンなど）と Bluetooth LE（マウス・キーボードの一部など）の両方を見張る。
        foreach (string selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(selector, [IsConnected], DeviceInformationKind.AssociationEndpoint);
                bool enumerated = false;   // 始めたときの一覧（今ある機器すべての Added）は知らせない
                watcher.EnumerationCompleted += (_, _) => enumerated = true;
                watcher.Added += (_, _) => { if (enumerated) Changed?.Invoke(); };
                watcher.Removed += (_, _) => { if (enumerated) Changed?.Invoke(); };
                watcher.Updated += (_, update) => { if (update.Properties.ContainsKey(IsConnected)) Changed?.Invoke(); };
                watcher.Start();
                watchers.Add(watcher);
            }
            catch (Exception ex) { log.Write($"Bluetooth の接続の見張りを始められません: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        foreach (var watcher in watchers)
            try { if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop(); }
            catch (Exception ex) { log.Write($"Bluetooth の接続の見張りを止められません: {ex.Message}"); }
        watchers.Clear();
    }
}
