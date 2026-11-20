using LogiGap.Core;

namespace LogiGap;

// LogiGap の通知領域のアイコンの表示・非表示をまとめて扱う。
//
// 11/19 ユーザー：「CPU 使用率、温度・メモリ使用率、マウス、キーボード、ヘッドホンの順で固定して」。
// いったん「全部隠して、右に来るものから順に出し直す」作りを試したが、実機では並びが変わらず、
// 「常に表示」になっていないアイコンは、出し直すと隠れているアイコンの中に入ってしまった。
// 調べると、Windows 11 の並びは「設定（個人用設定 → タスク バー → その他のシステム トレイ アイコン）で表示にした順番」で決まり、
// アプリからは並べ替えられない（The Old New Thing 2018/06/14 ほか）。そのため、出し直しはやめ、
// 並びはユーザーが設定でオンにする順番で決めてもらう。LogiGap 側は、アイコンの番号（Windows が覚える識別）を
// 機器の種類ごとに固定して、一度決めた並びが起動のたびに崩れないようにする（BatteryModule の枠）。
internal sealed class TrayOrder(ILog log) : IDisposable
{
    private readonly ILog log = log;

    // 小さい rank ほど左に置きたいもの（今は記録のためだけに持つ）。
    public void Register(NotifyIcon icon, int rank) { }

    public void Want(NotifyIcon icon, bool show)
    {
        if (icon.Visible != show) icon.Visible = show;
    }

    public void Dispose() { }
}
