// Windows の PDH から ACPI 温度を読みます。
// 読み取り元や失敗時の扱いを変えるときはここを編集します。
using System.Runtime.InteropServices;
using LogiGap.Core;

namespace LogiGap;

// Windows のパフォーマンスカウンター（PDH）で ACPI の温度を読む。管理者権限もドライバーも要らない。
internal sealed class AcpiTemperature : IDisposable
{
    private const uint FormatDouble = 0x00000200, FormatNoCap100 = 0x00008000, MoreData = 0x800007D2;
    private nint query, counter;
    public CpuTemperature? Read()
    {
        try
        {
            if (query == 0)
            {
                if (PdhOpenQueryW(null, 0, out query) != 0) { query = 0; return null; }
                if (PdhAddEnglishCounterW(query, @"\Thermal Zone Information(*)\Temperature", 0, out counter) != 0) { Close(); return null; }
            }
            if (PdhCollectQueryData(query) != 0) return null;
            uint size = 0;
            if (PdhGetFormattedCounterArrayW(counter, FormatDouble | FormatNoCap100, ref size, out _, 0) != MoreData || size == 0) return null;
            nint buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, FormatDouble | FormatNoCap100, ref size, out uint count, buffer) != 0) return null;
                // PDH_FMT_COUNTERVALUE_ITEM_W（x64）：名前のポインター 8、状態 4、詰め物 4、値 8 ＝ 24 バイト。
                var kelvins = new List<double>();
                for (int i = 0; i < count; i++)
                {
                    nint item = buffer + i * 24;
                    uint status = (uint)Marshal.ReadInt32(item, 8);
                    if (status is 0 or 1) kelvins.Add(BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16)));
                }
                return MonitorMath.FromAcpi(kelvins);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException) { return null; }
    }
    private void Close() { if (query != 0) PdhCloseQuery(query); query = counter = 0; }
    public void Dispose() => Close();
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nint user, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nint user, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}

