// 自分と候補ウィンドウの昇格状態をプロセストークンから読みます。
// 管理者ウィンドウの除外条件やトークン取得失敗時の扱いを変えるときに開きます。
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PartsTray;

internal static class ProcessElevation
{
    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool IsWindowElevated(nint window)
    {
        GetWindowThreadProcessId(window, out uint id);
        nint process = OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) return true;
        nint token = 0;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out token)) return true; // TOKEN_QUERY
            return !GetTokenInformation(token, 20, out int elevated, sizeof(int), out _) || elevated != 0;
        }
        finally { if (token != 0) CloseHandle(token); CloseHandle(process); }
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint id);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(nint token, int infoClass, out int info, int size, out int returned);
}
