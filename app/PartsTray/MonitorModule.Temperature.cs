// 温度の読み取りと ACPI の健全性判定をつなぎます。
// 読み取り元・判定への入力・Core Temp の共有メモリを変えるときに開きます。
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class MonitorModule
{
    private void SampleTemperature(double? usage)
    {
        if (!TempEnabled) temperature = null;
        else if (settings.TempMemory.Source == "coretemp")
            temperature = temperatureHealth.Observe("coretemp", ReadTemperature(), null);
        // ACPI の温度は 1℃ 刻みでゆっくり変わるので、1 秒に 1 回だけ読む（CPU・メモリは sampleIntervalMs ごと）。
        else if (elapsed.ElapsedMilliseconds - acpiReadAt >= 1000)
        {
            acpiReadAt = elapsed.ElapsedMilliseconds;
            rawAcpi = acpi.Read();
        }
        if (TempEnabled && settings.TempMemory.Source == "acpi")
            temperature = temperatureHealth.Observe("acpi", rawAcpi, usage);
    }

    private static CpuTemperature? ReadTemperature()
    {
        // 要確認: 終了した Core Temp のマッピングを保持し続けないよう、各サンプルで開き直し、その場で閉じる。
        using var local = Native.OpenFileMapping(4, false, "CoreTempMappingObjectEx");
        if (!local.IsInvalid) return ReadMapping(local);
        using var global = Native.OpenFileMapping(4, false, @"Global\CoreTempMappingObjectEx");
        return global.IsInvalid ? null : ReadMapping(global);
    }

    private static CpuTemperature? ReadMapping(SafeFileHandle mapping)
    {
        // 必要な先頭部分だけをマップし、電力などの拡張フィールドは読まない。
        nint view = Native.MapViewOfFile(mapping, 4, 0, 0, 2688);
        if (view == 0) return null;
        try
        {
            byte[] bytes = new byte[2688];
            Marshal.Copy(view, bytes, 0, bytes.Length);
            return MonitorMath.ReadCoreTemp(bytes);
        }
        finally { Native.UnmapViewOfFile(view); }
    }

}
