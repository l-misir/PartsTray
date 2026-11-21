// USB でつないだ Android などの電池を、Windows のファイルのやり取りの仕組み（WPD・MTP）から読みます。
// 読み直す間隔や、読む値を変えるときはここを編集します。値の直し方は PartsTray.Core/PortableBattery.cs。
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Devices.Enumeration;
using PartsTray.Core;

namespace PartsTray;

// 11/20 ユーザー：「Android もです」「USB 接続ですね」。Android は「ファイル転送」にすると、Windows の WPD の機器になり、
// 電池の残量（WPD_DEVICE_POWER_LEVEL）と電源の種類（WPD_DEVICE_POWER_SOURCE）を知らせる（Android の MTP の決まり）。
// 「充電のみ」のときは WPD の機器にならないので読めない。iPhone・iPad は WPD では残量を知らせない（11/20 実機）ので、
// Apple のサービス経由（AppleDeviceBattery.cs）で読み、ここでは数えない。
// 要確認：Android の実機では未確認（11/20 時点で手元にない）。
internal sealed class PortableDeviceBattery : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(30);
    private static readonly Guid DeviceProperties = new("26D4979A-E643-4626-9E2B-736DC0C92FDC"), ClientInfo = new("204D9F0C-2292-4080-9F42-40664E70F859");
    private const uint PowerLevel = 4, PowerSource = 5, Manufacturer = 7, Model = 8, FriendlyName = 12, DeviceType = 15;
    private const string WpdInterface = "{6AC27878-A6FA-4155-BA85-F98F491D4F33}";   // GUID_DEVINTERFACE_WPD
    private readonly ILog log;
    private readonly System.Threading.Timer timer;
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);
    private DeviceWatcher? watcher;
    private int reading;
    private volatile bool disposed, enabled;

    // 読み終えたら、そのとき USB でつながっていて残量を知らせる機器の一覧を知らせる（別のスレッドから）。
    public event Action<IReadOnlyList<BatteryDevice>>? Read;

    public PortableDeviceBattery(ILog log)
    {
        this.log = log;
        timer = new(_ => Start(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value;
        if (value) { timer.Change(0, (int)Every.TotalMilliseconds); Watch(); }
        else { timer.Change(Timeout.Infinite, Timeout.Infinite); Read?.Invoke([]); }
    }

    // WPD の機器が増えた・減ったら、2 秒後に読み直す（つないだ直後は機器の準備に少しかかる）。
    private void Watch()
    {
        if (watcher != null) return;
        try
        {
            var w = DeviceInformation.CreateWatcher($"System.Devices.InterfaceClassGuid:=\"{WpdInterface}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True");
            bool enumerated = false;
            w.EnumerationCompleted += (_, _) => enumerated = true;
            w.Added += (_, _) => { if (enumerated && enabled) timer.Change(2000, (int)Every.TotalMilliseconds); };
            w.Removed += (_, _) => { if (enumerated && enabled) timer.Change(500, (int)Every.TotalMilliseconds); };
            w.Start();
            watcher = w;
        }
        catch (Exception ex) { log.Write($"USB の機器の見張りを始められません: {ex.Message}"); }
    }

    // WPD は COM なので、読むたびに STA のスレッドを作って読む。前の読み取りが終わっていなければ飛ばす。
    private void Start()
    {
        if (disposed || !enabled || Interlocked.Exchange(ref reading, 1) != 0) return;
        var thread = new Thread(ReadNow) { IsBackground = true, Name = "PartsTray portable devices" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void ReadNow()
    {
        try
        {
            var result = new List<BatteryDevice>();
            IPortableDeviceManager? manager = null;
            try
            {
                manager = (IPortableDeviceManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("0af10cec-2ecd-4b92-9581-34f6ae0637f3"))!)!;
                uint count = 0;
                if (manager.GetDevices(null, ref count) < 0 || count == 0) { Read?.Invoke(result); return; }
                var ids = new nint[count];
                if (manager.GetDevices(ids, ref count) < 0) { Read?.Invoke(result); return; }
                foreach (nint p in ids.Take((int)count))
                {
                    string id = Marshal.PtrToStringUni(p) ?? ""; Marshal.FreeCoTaskMem(p);
                    try { if (ReadDevice(id) is { } device) result.Add(device); }
                    catch (Exception ex) { log.Write($"USB の機器の電池を読めません: {ex.GetType().Name}"); }
                }
            }
            catch (Exception ex) { log.Write($"USB の機器の一覧を読めません: {ex.GetType().Name} 0x{ex.HResult:X8}"); }   // 自分で作ったスレッドなので、ここで止めないとアプリごと落ちる
            finally { if (manager != null) Marshal.ReleaseComObject(manager); }
            Read?.Invoke(result);
        }
        finally { Volatile.Write(ref reading, 0); }
    }

    private BatteryDevice? ReadDevice(string id)
    {
        var device = (IPortableDevice)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("728a21c5-3d9e-48d7-9810-864848f0f404"))!)!;
        var client = (IPortableDeviceValues)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("0c15d503-d017-47ce-9016-7b3f978721cc"))!)!;
        IPortableDeviceContent? content = null; IPortableDeviceProperties? properties = null; IPortableDeviceValues? values = null;
        try
        {
            SetString(client, 2, "PartsTray"); SetNumber(client, 3, 1); SetNumber(client, 4, 0); SetNumber(client, 5, 0);
            SetNumber(client, 8, 0x20000);       // WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE = SECURITY_IMPERSONATION
            SetNumber(client, 9, 0x80000000);    // WPD_CLIENT_DESIRED_ACCESS = GENERIC_READ（読むだけ）
            if (device.Open(id, client) < 0) return null;
            try
            {
                if (device.Content(out content) < 0 || content.Properties(out properties) < 0 || properties.GetValues("DEVICE", 0, out values) < 0) return null;
                if (PortableBattery.IsApple(GetString(values, Manufacturer))) return null;
                if (PortableBattery.ToReading(GetNumber(values, PowerLevel), GetNumber(values, PowerSource)) is not { } battery) return null;
                string name = GetString(values, FriendlyName) is { Length: > 0 } friendly ? friendly : GetString(values, Model) ?? "USB の機器";
                var kind = PortableBattery.Kind(GetNumber(values, DeviceType));
                // 機器の番号（中に製造番号が入る）は、そのまま持たずに縮めた値にする。記録にも残さない。
                string key = "usb:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0, 8);
                if (logged.Add(key)) log.Write($"USB の機器の電池を、ファイルのやり取りの仕組み（WPD）で読めました（{battery.Percent}%・{battery.Charge}）");
                return new BatteryDevice(key, name, kind, true, battery, DateTime.UtcNow);
            }
            finally { device.Close(); }
        }
        finally
        {
            if (values != null) Marshal.ReleaseComObject(values);
            if (properties != null) Marshal.ReleaseComObject(properties);
            if (content != null) Marshal.ReleaseComObject(content);
            Marshal.ReleaseComObject(client); Marshal.ReleaseComObject(device);
        }
    }

    private static void SetString(IPortableDeviceValues values, uint pid, string value) { var key = new PropertyKey(ClientInfo, pid); values.SetStringValue(ref key, value); }
    private static void SetNumber(IPortableDeviceValues values, uint pid, uint value) { var key = new PropertyKey(ClientInfo, pid); values.SetUnsignedIntegerValue(ref key, value); }
    private static uint? GetNumber(IPortableDeviceValues values, uint pid) { var key = new PropertyKey(DeviceProperties, pid); return values.GetUnsignedIntegerValue(ref key, out uint v) == 0 ? v : null; }
    private static string? GetString(IPortableDeviceValues values, uint pid)
    {
        var key = new PropertyKey(DeviceProperties, pid);
        if (values.GetStringValue(ref key, out nint p) != 0 || p == 0) return null;
        try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; enabled = false;
        timer.Dispose();
        try { if (watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop(); }
        catch (Exception ex) { log.Write($"USB の機器の見張りを止められません: {ex.Message}"); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid fmtid, uint pid) { public Guid Fmtid = fmtid; public uint Pid = pid; }

    [ComImport, Guid("a1567595-4c2f-4574-a6fa-ecef917b9a40"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPortableDeviceManager { [PreserveSig] int GetDevices([In, Out, MarshalAs(UnmanagedType.LPArray)] nint[]? ids, ref uint count); }

    [ComImport, Guid("625e2df8-6392-4cf0-9ad1-3cfa5f17775c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPortableDevice
    {
        [PreserveSig] int Open([MarshalAs(UnmanagedType.LPWStr)] string id, IPortableDeviceValues client);
        [PreserveSig] int SendCommand(uint flags, nint parameters, out nint results);
        [PreserveSig] int Content(out IPortableDeviceContent content);
        [PreserveSig] int Capabilities(out nint capabilities);
        [PreserveSig] int Cancel();
        [PreserveSig] int Close();
    }

    [ComImport, Guid("6a96ed84-7c73-4480-9938-bf5af477d426"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPortableDeviceContent
    {
        [PreserveSig] int EnumObjects(uint flags, [MarshalAs(UnmanagedType.LPWStr)] string parent, nint filter, out nint e);
        [PreserveSig] int Properties(out IPortableDeviceProperties properties);
    }

    [ComImport, Guid("7f6d695c-03df-4439-a809-59266beee3a6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPortableDeviceProperties
    {
        [PreserveSig] int GetSupportedProperties([MarshalAs(UnmanagedType.LPWStr)] string objectId, out nint keys);
        [PreserveSig] int GetPropertyAttributes([MarshalAs(UnmanagedType.LPWStr)] string objectId, ref PropertyKey key, out nint attributes);
        [PreserveSig] int GetValues([MarshalAs(UnmanagedType.LPWStr)] string objectId, nint keys, out IPortableDeviceValues values);
    }

    [ComImport, Guid("6848f6f2-3155-4f86-b6f5-263eeeab3143"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPortableDeviceValues
    {
        [PreserveSig] int GetCount(ref uint count);
        [PreserveSig] int GetAt(uint index, nint key, nint value);
        [PreserveSig] int SetValue(ref PropertyKey key, nint value);
        [PreserveSig] int GetValue(ref PropertyKey key, nint value);
        [PreserveSig] int SetStringValue(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int GetStringValue(ref PropertyKey key, out nint value);
        [PreserveSig] int SetUnsignedIntegerValue(ref PropertyKey key, uint value);
        [PreserveSig] int GetUnsignedIntegerValue(ref PropertyKey key, out uint value);
    }
}
