// 通知アイコンの番号を定義し、生成後の番号を確認します。
// Windows が記憶する番号の互換性を保つための表です。
using System.Reflection;
using PartsTray.Core;

namespace PartsTray;

// Windows が「アプリ＋番号」で表示設定と並びを覚えるので、この生成順を変えない。
// 1: TrayContext の本体（温度、または温度・メモリの 2 段）
// 2: MonitorModule のオウム
// 3〜8: MonitorModule で非表示のまま作って破棄（旧電池の番号 3・4 も再利用しない）
// 9: CPU 使用率、10: メモリ（無効な設定でも生成して番号を確保）
// 11〜22: BatteryModule の電池枠（11〜13 は従来どおり。動的なウィンドウ用アイコンは 23 以降）
internal static class TrayIds
{
    public const int Main = 1, Parrot = 2, UnusedFirst = 3, UnusedCount = 6, Cpu = 9, Memory = 10;
    public const int BatteryFirst = 11, BatteryCount = BatteryLayout.SlotCount;

    public static void Check(NotifyIcon icon, int expected, ILog log)
    {
        try
        {
            // 要確認: WinForms の通し番号と非公開 _id に依存する。取得不能なら黙って続け、不一致だけ記録する。
            object? value = typeof(NotifyIcon).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(icon);
            if (value is null) return;
            long actual = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            if (actual != expected) log.Write($"要確認: NotifyIcon の番号は {actual}（期待 {expected}）です");
        }
        catch (Exception) { } // 診断の失敗でアイコンの動作を変えない。
    }
}
