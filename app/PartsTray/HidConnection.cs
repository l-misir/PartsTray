using System.ComponentModel;
using System.Runtime.InteropServices;
using PartsTray.Core;
using Microsoft.Win32.SafeHandles;

namespace PartsTray;

internal sealed class HidConnection : IReportTransport, IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly FileStream shortStream, longStream;
    private readonly SafeFileHandle shortWriter, longWriter;
    private readonly ILog log;
    private readonly Task[] readers;
    private int disposed;
    public ReceiverEndpoints Endpoints { get; }
    public event Action<byte[]>? Received;
    public event Action? Lost;

    public HidConnection(ReceiverEndpoints endpoints, ILog log)
    {
        Endpoints = endpoints; this.log = log;
        shortStream = Open(endpoints.Short.Path);
        try { longStream = Open(endpoints.Long.Path); }
        catch { shortStream.Dispose(); throw; }
        try
        {
            shortWriter = OpenWriter(endpoints.Short.Path);
            try { longWriter = OpenWriter(endpoints.Long.Path); }
            catch { shortWriter.Dispose(); throw; }
        }
        catch { shortStream.Dispose(); longStream.Dispose(); throw; }
        // Start explicitly after event handlers have been attached.
        readers = new Task[2];
    }
    private static FileStream Open(string path)
    {
        var h = NativeHid.CreateFileW(path, 0xC0000000, 3, 0, 3, 0x40000000, 0);
        if (h.IsInvalid) { h.Dispose(); throw new Win32Exception(); }
        try { return new FileStream(h, FileAccess.ReadWrite, 1, true); }
        catch { h.Dispose(); throw; }
    }
    private static SafeFileHandle OpenWriter(string path)
    {
        // Separate, unbound handles: a manually allocated OVERLAPPED must not enter FileStream's IOCP.
        var h = NativeHid.CreateFileW(path, 0x40000000, 3, 0, 3, 0x40000000, 0);
        if (h.IsInvalid) { h.Dispose(); throw new Win32Exception(); }
        return h;
    }
    public void Start()
    {
        readers[0] = Read(shortStream, Endpoints.Short.InputLength);
        readers[1] = Read(longStream, Endpoints.Long.InputLength);
    }
    private async Task Read(FileStream stream, int size)
    {
        byte[] buffer = new byte[size];
        try
        {
            while (!stop.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, stop.Token).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException("HID input closed");
                try { Received?.Invoke(buffer.AsSpan(0, n).ToArray()); }
                catch (Exception ex) { log.Write($"受信リスナー: {ex}"); }
            }
        }
        catch (Exception ex) when (stop.IsCancellationRequested && ex is OperationCanceledException or ObjectDisposedException or IOException) { }
        catch (Exception ex) { log.Write($"HID 読取エラー: {ex}"); Lost?.Invoke(); }
    }

    public void Send(byte[] report)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        // 要確認: 指示書Kの swid 7 を同じ送信口へ通すため、許可ファイル一覧外のこの検査も最小限拡張。読み取りの許可リストは電池追跡器で検査する。
        if (report.Length is not (7 or 20) || report[0] is not (0x10 or 0x11) || report[1] is < 1 or > 6 ||
            ((report[3] & 15) != 5 && !(report.Length == 20 && report[0] == 0x11 && (report[3] & 15) == 7)))
            throw new InvalidOperationException("HID 出力フレーム拒否");
        var writer = report[0] == 0x10 ? shortWriter : longWriter;
        int length = report[0] == 0x10 ? Endpoints.Short.OutputLength : Endpoints.Long.OutputLength;
        var payload = new byte[length]; report.CopyTo(payload, 0);
        // Submit the native overlapped write NOW, while GapSession's gate is held.
        // The completion waiter can run later; it never submits another write.
        var write = new OverlappedWrite(writer, payload);
        try { write.Submit(); }
        catch { write.Dispose(); throw; }
        _ = Task.Run(() =>
        {
            using (write)
            {
                try { write.Wait(); }
                catch (Exception ex) { if (!stop.IsCancellationRequested) { log.Write($"HID 書込エラー: {ex}"); Lost?.Invoke(); } }
            }
        });
    }

    private sealed class OverlappedWrite : IDisposable
    {
        private readonly SafeFileHandle handle;
        private readonly GCHandle buffer;
        private readonly EventWaitHandle done = new(false, EventResetMode.ManualReset);
        private readonly nint overlapped;
        private readonly uint length;
        private bool reference;
        [StructLayout(LayoutKind.Sequential)] private struct Overlapped
        { public nuint Internal, InternalHigh; public uint Offset, OffsetHigh; public nint Event; }
        public OverlappedWrite(SafeFileHandle handle, byte[] payload)
        {
            this.handle = handle; length = (uint)payload.Length;
            handle.DangerousAddRef(ref reference);
            buffer = GCHandle.Alloc(payload, GCHandleType.Pinned);
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
            Marshal.StructureToPtr(new Overlapped { Event = done.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
        }
        public void Submit()
        {
            if (!NativeHid.WriteFile(handle, buffer.AddrOfPinnedObject(), length, 0, overlapped) && Marshal.GetLastWin32Error() != 997)
                throw new Win32Exception();
        }
        public void Wait()
        {
            if (!NativeHid.GetOverlappedResult(handle, overlapped, out uint written, true)) throw new Win32Exception();
            if (written != length) throw new IOException("HID short write");
        }
        public void Dispose()
        {
            Marshal.FreeHGlobal(overlapped); buffer.Free(); done.Dispose();
            if (reference) { reference = false; handle.DangerousRelease(); }
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        NativeHid.CancelIoEx(shortStream.SafeFileHandle, 0); NativeHid.CancelIoEx(longStream.SafeFileHandle, 0);
        NativeHid.CancelIoEx(shortWriter, 0); NativeHid.CancelIoEx(longWriter, 0);
        shortStream.Dispose(); longStream.Dispose();
        shortWriter.Dispose(); longWriter.Dispose();
    }
}
