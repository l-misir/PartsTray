// Windows の Bluetooth 電池プロパティを読み取ります。
// 列挙と接続判定を変える場合はここを編集します。
using System.ComponentModel;
using System.Text;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal static class BluetoothBatteryReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    private static readonly PropertyKey BatteryKey = new(new("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly PropertyKey FriendlyNameKey = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly PropertyKey ContainerKey = new(new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);   // DEVPKEY_Device_ContainerId
    private static readonly PropertyKey CompatibleKey = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 4);
    private static readonly PropertyKey ClassKey = new(new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 10);
    private static readonly PropertyKey ClassFallbackKey = new(new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 4);

    private sealed class Evidence
    {
        public HashSet<string> CompatibleIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public uint ClassOfDevice;
    }

    // 接続判定用とは別に、存在しない部品も列挙する。切断中の HID の種類を失わない。
    private static Dictionary<Guid, Evidence> ReadKinds()
    {
        nint set = SetupDiGetClassDevsW(0, null, 0, 4); // DIGCF_ALLCLASSES（PRESENT を付けない）
        if (set == -1) throw new Win32Exception();
        var result = new Dictionary<Guid, Evidence>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _)) continue;
                string instance = id.ToString();
                bool hid = instance.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase);
                bool bluetooth = instance.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase);
                if (!hid && !bluetooth) continue;
                if (Property(set, ref info, ContainerKey, 0x0D) is not { Length: 16 } container) continue;
                var key = new Guid(container);
                if (key == Guid.Empty) continue;
                if (!result.TryGetValue(key, out var evidence)) result.Add(key, evidence = new());
                if (hid && Property(set, ref info, CompatibleKey, 0x2012) is { } ids) // STRING_LIST
                    evidence.CompatibleIds.UnionWith(Encoding.Unicode.GetString(ids).Split('\0', StringSplitOptions.RemoveEmptyEntries));
                if (bluetooth)
                {
                    var bytes = Property(set, ref info, ClassKey, 7) ?? Property(set, ref info, ClassFallbackKey, 7);
                    if (bytes is { Length: 4 }) evidence.ClassOfDevice = BitConverter.ToUInt32(bytes);
                }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    // つながっているかの見分け方（11/20 実機で直した）：
    // 電池の値を持つ「Hands-Free AG」の機器は、ペアリングしてあれば切断中も「動作中（DN_STARTED）」のままで、
    // 電池の値も最後の値（WH-1000XM6 は 11/17 の 56%）が残る。そのため DN_STARTED では切断を見分けられず、
    // 切断中のヘッドホンが表示され続けた（11/20 ユーザー：「今ヘッドホンつながってないです。本来は非表示になるはず」）。
    // 一方、音の出入り口（SWD\MMDEVAPI\…、「ヘッドホン (WH-1000XM6)」など）は、つながっている間だけ現れる。
    // 同じ機器の部品は同じ「入れ物の ID」（ContainerId）を持つので、同じ入れ物に音の出入り口（または HID）が
    // 現れていれば「つながっている」とする。入れ物の ID が読めない機器だけ、前の DN_STARTED で判断する。
    // 要確認: つながったときに音の出入り口が現れることは、まだ実機で確かめていない（11/20 は切断中だけ確認）。
    public static IReadOnlyList<BatteryDevice> Read()
    {
        var kinds = ReadKinds();
        nint set = SetupDiGetClassDevsW(0, null, 0, 2 | 4);   // DIGCF_PRESENT | DIGCF_ALLCLASSES
        if (set == -1) throw new Win32Exception();
        var found = new List<(string Id, string Name, Guid? Container, bool Started, byte? Battery)>();
        var live = new HashSet<Guid>();   // つながっている間だけ現れる部品がある入れ物
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _)) continue;
                string instance = id.ToString();
                bool liveKind = instance.StartsWith(@"SWD\MMDEVAPI\", StringComparison.OrdinalIgnoreCase) ||
                    instance.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase);
                // Bluetooth の機器そのものの部品。AirPods は電池の値を持たないので、名前で見つけて電池なしで返す（値は AirPodsWatcher が埋める）。
                bool device = instance.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) ||
                    instance.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);
                var battery = Property(set, ref info, BatteryKey, 3); // HID 自身に電池がある機器も読む。DEVPROP_TYPE_BYTE
                bool hasBattery = battery is { Length: 1 } && battery[0] <= 100;
                if (!liveKind && !hasBattery && !device) continue;
                Guid? container = Property(set, ref info, ContainerKey, 0x0D) is { Length: 16 } bytes ? new Guid(bytes) : null; // DEVPROP_TYPE_GUID
                if (container == Guid.Empty) container = null;
                if (liveKind && container is { } c) live.Add(c);
                if (!hasBattery && !device) continue;
                var nameBytes = Property(set, ref info, FriendlyNameKey, 0x12); // DEVPROP_TYPE_STRING
                // 要確認: 名前を取れない機器は PnP インスタンス ID で表示。識別子もこれを使い、同名の別機器を混同しない。
                string name = nameBytes == null ? instance : BatteryMath.BluetoothName(Encoding.Unicode.GetString(nameBytes).TrimEnd('\0'));
                if (!hasBattery && !IsAirPods(name)) continue;
                bool started = CM_Get_DevNode_Status(out uint status, out _, info.DevInst, 0) == 0 && (status & 8) != 0;
                found.Add((instance, name, container, started, hasBattery ? battery![0] : null));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return found.Select(f =>
        {
            var evidence = f.Container is { } id ? kinds.GetValueOrDefault(id) : null;
            var kind = BatteryKinds.Detect(f.Name, compatibleIds: evidence?.CompatibleIds,
                classOfDevice: evidence?.ClassOfDevice ?? 0);
            return new BatteryDevice($"bluetooth:{f.Id}", f.Name, kind,
                f.Container is { } c ? live.Contains(c) : f.Started,
                f.Battery is { } percent ? new(percent, BatteryLevel.Unknown, BatteryCharge.Unknown, false, false) : null,
                f.Battery is null ? null : DateTime.UtcNow);
        }).ToList();
    }
    // AirPods（電池の値を Windows が持たない機器）かどうか。名前は利用者が変えられるので、「AirPods」を含むかだけで見る。
    public static bool IsAirPods(string name) => name.Contains("AirPods", StringComparison.OrdinalIgnoreCase);
    // 識別子（bluetooth: と PnP インスタンス ID）から Bluetooth のアドレス（12 桁の 16 進）を取り出す。
    // 例：…\DEV_0123456789AB\…、…&0&0123456789AB_C00000000（アドレスは例）
    public static bool TryGetAddress(string id, out ulong address)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id, @"DEV_([0-9A-F]{12})|&([0-9A-F]{12})_C[0-9A-F]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        address = 0;
        return m.Success && ulong.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out address);
    }
    private static byte[]? Property(nint set, ref DeviceInfo info, PropertyKey key, uint expectedType)
    {
        SetupDiGetDevicePropertyW(set, ref info, in key, out uint type, null, 0, out uint size, 0);
        if (Marshal.GetLastWin32Error() != 122 || size == 0 || size > 65536 || type != expectedType) return null;
        byte[] buffer = new byte[size];
        return SetupDiGetDevicePropertyW(set, ref info, in key, out type, buffer, size, out uint used, 0) &&
            type == expectedType && used == size ? buffer : null;
    }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevsW(nint classGuid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDevicePropertyW(nint set, ref DeviceInfo info,
        in PropertyKey key, out uint type, [Out] byte[]? buffer, uint size, out uint requiredSize, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInstanceIdW(nint set, ref DeviceInfo info,
        StringBuilder id, uint size, out uint requiredSize);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
