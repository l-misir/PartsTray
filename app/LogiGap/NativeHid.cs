using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LogiGap;

internal sealed record HidEndpoint(string Path, Guid Container, ushort Usage, ushort InputLength, ushort OutputLength);
internal sealed record ReceiverEndpoints(HidEndpoint Short, HidEndpoint Long);

internal static class NativeHid
{
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData
    { public uint Size; public Guid ClassGuid; public uint Flags; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo
    { public uint Size; public Guid ClassGuid; public uint DevInst; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes
    { public uint Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey
    { public Guid Format; public uint Id; }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint data, nint caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevsW(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint list, nint info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(nint list, ref InterfaceData data, nint detail, uint size, out uint required, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(nint list, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(SafeFileHandle handle, nint overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteFile(SafeFileHandle handle, nint buffer, uint count, nint written, nint overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetOverlappedResult(SafeFileHandle handle, nint overlapped, out uint count, [MarshalAs(UnmanagedType.Bool)] bool wait);

    public static ReceiverEndpoints? Find()
    {
        HidD_GetHidGuid(out var guid);
        nint list = SetupDiGetClassDevsW(ref guid, null, 0, 0x12);
        if (list == -1) throw new Win32Exception();
        List<HidEndpoint> found = [];
        try
        {
            for (uint i = 0; ; i++)
            {
                var iface = new InterfaceData { Size = (uint)Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(list, 0, ref guid, i, ref iface))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception();
                }
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                SetupDiGetDeviceInterfaceDetailW(list, ref iface, 0, 0, out uint size, ref info);
                if (size < 8 || size > 65536) continue;
                nint detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(list, ref iface, detail, size, out _, ref info)) continue;
                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path == null || !path.Contains("vid_046d&pid_c548", StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = CreateFileW(path, 0, 3, 0, 3, 0, 0);
                    if (handle.IsInvalid) continue;
                    var attr = new Attributes { Size = (uint)Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attr) || attr.Vendor != 0x046D || attr.Product != 0xC548) continue;
                    if (!HidD_GetPreparsedData(handle, out nint pp)) continue;
                    nint caps = Marshal.AllocHGlobal(64);
                    try
                    {
                        if (HidP_GetCaps(pp, caps) != 0x00110000) continue;
                        ushort usage = (ushort)Marshal.ReadInt16(caps), page = (ushort)Marshal.ReadInt16(caps, 2);
                        ushort input = (ushort)Marshal.ReadInt16(caps, 4), output = (ushort)Marshal.ReadInt16(caps, 6);
                        if (page != 0xFF00 || usage is not (1 or 2) || input < (usage == 1 ? 7 : 20) || output < (usage == 1 ? 7 : 20)) continue;
                        var key = new PropertyKey { Format = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };
                        byte[] container = new byte[16];
                        if (!SetupDiGetDevicePropertyW(list, ref info, ref key, out uint type, container, 16, out uint required, 0) || type != 13 || required != 16) continue;
                        found.Add(new(path, new Guid(container), usage, input, output));
                    }
                    finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(pp); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }
        // A pair must belong to the same physical receiver; never mix identical VID/PID receivers.
        foreach (var group in found.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).GroupBy(e => e.Container))
        {
            var s = group.FirstOrDefault(e => e.Usage == 1); var l = group.FirstOrDefault(e => e.Usage == 2);
            if (s != null && l != null && group.Key != Guid.Empty) return new(s, l);
        }
        return null;
    }
}
