// ホイール選択専用の Windows マウスフックとメッセージ投函の宣言をまとめます。
// OS とのデータの受け渡しを変えるときに開きます。ジェスチャーのフックは変更しません。
using System.Runtime.InteropServices;

namespace PartsTray;

internal sealed partial class WindowKeysModule
{
    private delegate nint MouseHookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookInput
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, MouseHookProc callback, nint module, uint thread);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
