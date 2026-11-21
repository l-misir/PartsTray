// キーの直後・設定したボタンの保持中だけホイールを取り込み、UI スレッドで前後へ切り替えます。
// フックの寿命、時間切れ、切り替えの投函を変えるときに開きます。
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

internal sealed partial class WindowKeysModule
{
    private const int WmWheelSwitch = 0x8000 + 51, WmWheelReport = 0x8000 + 52, WmDeviceWheel = 0x8000 + 53;
    private readonly WindowSwitchWheel wheel = new(() => TimeSpan.FromMilliseconds(Environment.TickCount64));
    private readonly System.Windows.Forms.Timer wheelTimer = new() { Interval = 25 };
    private readonly MouseHookProc wheelHookCallback;
    private readonly Queue<string> wheelReports = new();
    private nint wheelHook;
    private volatile int wheelGeneration;
    private volatile bool wheelSession;

    private void SwitchFromKey(bool previous)
    {
        EndWheelSelection();
        switcher.BeginSession();
        switcher.Switch(previous);
        if (current is not { } settings || !wheel.Start(settings)) return;
        InstallWheelHook();
    }

    private void InstallWheelHook()
    {
        wheelGeneration++;
        wheelSession = true;
        // WM_HOTKEY と同じ UI スレッドで設置する。ジェスチャーの有効・無効には依存しない。
        wheelHook = SetWindowsHookEx(14, wheelHookCallback, GetModuleHandle(null), 0);
        if (wheelHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            EndWheelSelection();
            log.Write($"ホイールで切り替えるフックを登録できません（エラー {error}）");
            return;
        }
        wheelTimer.Start();
    }

    private void CheckWheelTimeout()
    {
        try
        {
            if (SelectionExpired() || !wheel.IsActive) EndWheelSelection();
            FlushWheelReports();
        }
        catch { EndWheelSelection(deferReport: true); }
    }

    private void EndWheelSelection(bool deferReport = false)
    {
        holdSelection.Stop();
        wheel.Stop();
        wheelTimer.Stop();
        if (wheelHook != 0) { UnhookWindowsHookEx(wheelHook); wheelHook = 0; }
        if (wheelSession)
        {
            wheelSession = false;
            wheelGeneration++; // 終了した時間の未処理メッセージを次の選択へ持ち越さない。
            wheelReports.Enqueue((holdSession ? "ボタン＋" : "") + wheel.Summary);
        }
        holdSession = false;
        if (deferReport)
        {
            // フックではファイルへ書かない。投函失敗時も次の UI タイマーで回収する。
            if (!PostMessage(Handle, WmWheelReport, 0, 0)) wheelTimer.Start();
        }
        else FlushWheelReports();
    }

    private void FlushWheelReports()
    {
        while (wheelReports.TryDequeue(out string? report)) log.Write(report);
    }

    private nint WheelHook(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            try
            {
                var input = Marshal.PtrToStructure<MouseHookInput>(data);
                if (input.ExtraInfo == KeyStroke.ExtraInfo) return CallNextHookEx(0, code, message, data);
                if (SelectionExpired() || !wheel.IsActive)
                    EndWheelSelection(deferReport: true);
                else if (!holdSession && (int)message is 0x0201 or 0x0204 or 0x0207 or 0x020B) // 左・右・中・戻る/進むの押下
                {
                    wheel.ButtonDown(input.ExtraInfo);
                    EndWheelSelection(deferReport: true); // ボタンは必ずそのまま通す。
                }
                else if ((int)message is 0x020A or 0x020E)
                {
                    // injected フラグで除外しない（Options+ の横ホイールも受け取る）。
                    int delta = unchecked((short)(input.MouseData >> 16));
                    var result = wheel.Receive((int)message == 0x020E, delta, input.ExtraInfo);
                    if (!result.Handled) EndWheelSelection(deferReport: true);
                    else
                    {
                        if (result.Steps != 0 && !PostMessage(Handle, WmWheelSwitch, result.Steps, wheelGeneration))
                            throw new InvalidOperationException("切り替えの投函に失敗しました");
                        return 1;
                    }
                }
            }
            catch
            {
                // 入力を止めない。詳細な処理と記録はフックから戻った後で行う。
                wheelReports.Enqueue("ホイールで切り替える処理に失敗したため、入力を素通しに戻しました");
                EndWheelSelection(deferReport: true);
            }
        }
        return CallNextHookEx(0, code, message, data);
    }

    // 押下一覧と同じ UI キューで受け取り、解放より先に回転を処理する。
    public bool ReceiveDeviceRotation(short rotation)
    {
        if (!wheelSession) return false;
        var message = Message.Create(Handle, WmDeviceWheel, rotation, wheelGeneration);
        HandleWheelMessage(ref message);
        return true;
    }

    private bool HandleWheelMessage(ref Message m)
    {
        if (m.Msg is not (WmWheelSwitch or WmWheelReport or WmDeviceWheel)) return false;
        try
        {
            if (m.Msg == WmWheelReport) FlushWheelReports();
            else if (SelectionExpired()) EndWheelSelection();
            else if (wheel.IsActive && (int)m.LParam == wheelGeneration)
            {
                int steps = m.Msg == WmDeviceWheel
                    ? wheel.ReceiveDeviceRotation(unchecked((short)m.WParam)).Steps : (int)m.WParam;
                for (int i = 0; i < Math.Abs(steps); i++) switcher.Switch(steps < 0);
            }
        }
        catch (Exception ex)
        {
            EndWheelSelection();
            log.Write($"ホイールで切り替える処理に失敗: {ex.Message}");
        }
        return true;
    }

}
