// 権限の違う二重起動でも待機・設定画面の通知ができる同期オブジェクトを作ります。
// Mutex の所有権の受け渡しや、同期オブジェクトの権限を変えるときに開きます。
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PartsTray;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    public EventWaitHandle Show { get; }
    private bool owned;

    public SingleInstance(string sid)
    {
        mutex = new Mutex(false);
        try
        {
            mutex.SafeWaitHandle = Create("Local\\PartsTray.Instance." + sid, sid, false);
            Show = new EventWaitHandle(false, EventResetMode.AutoReset);
            try { Show.SafeWaitHandle = Create("Local\\PartsTray.Show." + sid, sid, true); }
            catch { Show.Dispose(); throw; }
        }
        catch { mutex.Dispose(); throw; }
    }

    public bool Acquire(int milliseconds = 0)
    {
        if (owned) return true;
        try { owned = mutex.WaitOne(milliseconds); }
        catch (AbandonedMutexException) { owned = true; }
        return owned;
    }
    public void Release() { if (owned) { mutex.ReleaseMutex(); owned = false; } }
    public void Dispose() { Release(); Show.Dispose(); mutex.Dispose(); }

    private static SafeWaitHandle Create(string name, string sid, bool isEvent)
    {
        // 通常権限の同一ユーザーには SYNCHRONIZE と状態変更だけ。低いラベルで通知を許す。
        uint access = isEvent ? 0x00100002u : 0x00100001u;
        string sddl = $"D:(A;;0x{access:X8};;;{sid})(A;;GA;;;SY)(A;;GA;;;BA)S:(ML;;NW;;;LW)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out nint descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            nint handle = isEvent ? CreateEventEx(ref attributes, name, 0, access) : CreateMutexEx(ref attributes, name, 0, access);
            if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new SafeWaitHandle(handle, true);
        }
        finally { LocalFree(descriptor); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes
    { public int Length; public nint Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateMutexExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateMutexEx(ref SecurityAttributes attributes, string name, uint flags, uint access);
    [DllImport("kernel32.dll", EntryPoint = "CreateEventExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateEventEx(ref SecurityAttributes attributes, string name, uint flags, uint access);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
