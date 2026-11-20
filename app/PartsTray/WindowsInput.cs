using System.ComponentModel;
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal sealed class WindowsInput : IInputSink
{
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard
    { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    public void Keys(string keys)
    {
        var sequence = InputSequence.ForKeys(keys).Select(k => new Input
        { Type = 1, Data = new() { Keyboard = new() { Vk = k.VirtualKey, Flags = k.Flags, Extra = KeyStroke.ExtraInfo } } }).ToArray();
        Send(sequence);
    }
    public void HorizontalWheel(int delta) => Send([new Input
    { Type = 0, Data = new() { Mouse = new() { Data = unchecked((uint)delta), Flags = 0x1000, Extra = KeyStroke.ExtraInfo } } }]);
    private static void Send(Input[] sequence)
    {
        if (SendInput((uint)sequence.Length, sequence, Marshal.SizeOf<Input>()) != sequence.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput の一部または全部が拒否されました（UIPI 等）");
    }
}
