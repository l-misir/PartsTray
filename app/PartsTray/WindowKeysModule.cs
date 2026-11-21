// ウィンドウ操作のキー（本当の最大化・最大化しているウィンドウの切り替え）を Windows に登録して、押されたら動かします。
// 動き方を変えるときはここを編集します。キーの組み合わせは設定の windowKeys（PartsTray.Core/Settings.cs）。
using System.Runtime.InteropServices;
using PartsTray.Core;

namespace PartsTray;

// 11/20 ユーザー：
//   ・「Windows＋↑ での擬似的な最大化じゃなくて、ちゃんとした最大化」：Windows＋↑ はスナップの操作なので、
//     画面の上半分に寄っていると変化しない、などがある。ここでは前面のウィンドウを直接最大化する（最大化していれば元に戻す）。
//   ・「最大化表示しているアプリを瞬時に切り替える」「最小化していったときに出てくる順番」：同じモニターで最大化しているウィンドウを、
//     重なっている順に切り替える。今のウィンドウを一番下に回すので、押すたびに全部を順に回れる。
// マウスのボタンに割り当てて使う前提（Options+ などでボタンにキーの組み合わせを割り当てる）。キーは最初は割り当てない。
internal sealed partial class WindowKeysModule : NativeWindow, IFeatureModule
{
    private const int WmHotkey = 0x0312, IdMaximize = 1, IdNextMaximized = 2, IdPreviousMaximized = 3;
    private readonly ILog log;
    private WindowKeySettings? current;
    private readonly MaximizedWindowSwitcher switcher;

    public WindowKeysModule(AppSettings settings, ILog log)
    {
        this.log = log; switcher = new(log);
        holdSelection = new(wheel, () => TimeSpan.FromMilliseconds(Environment.TickCount64));
        wheelHookCallback = WheelHook;
        wheelTimer.Tick += (_, _) => CheckWheelTimeout();
        CreateHandle(new CreateParams { Caption = "PartsTray window keys", Parent = new nint(-3) });   // 見えない窓（HWND_MESSAGE）
        Apply(settings.WindowKeys);
    }

    public IEnumerable<ToolStripItem> CreateMenuItems() => [];
    public void SettingsChanged(AppSettings settings) => Apply(settings.WindowKeys);

    private void Apply(WindowKeySettings keys)
    {
        if (keys == current) return;
        EndWheelSelection();
        UnregisterHotKey(Handle, IdMaximize); UnregisterHotKey(Handle, IdNextMaximized);
        UnregisterHotKey(Handle, IdPreviousMaximized);
        Register(IdMaximize, keys.Maximize, "本当の最大化");
        Register(IdNextMaximized, keys.NextMaximized, "最大化しているウィンドウの切り替え");
        Register(IdPreviousMaximized, keys.PreviousMaximized, "最大化しているウィンドウを前へ");
        current = keys;
        holdSelection.Configure(keys);
    }

    private void Register(int id, string text, string what)
    {
        if (!HotkeyText.TryParse(text, out uint modifiers, out uint key) || key == 0) return;
        if (RegisterHotKey(Handle, id, modifiers | HotkeyText.NoRepeat, key)) log.Write($"{what}のキーを登録しました（{text}）");
        else log.Write($"{what}のキー（{text}）を登録できません。ほかのアプリが同じキーを使っている可能性があります（エラー {Marshal.GetLastWin32Error()}）");
    }

    protected override void WndProc(ref Message m)
    {
        if (HandleWheelMessage(ref m)) return;
        if (m.Msg == WmHotkey)
        {
            try
            {
                if (m.WParam == IdMaximize) MaximizedWindowSwitcher.ToggleMaximize();
                else if (m.WParam == IdNextMaximized) SwitchFromKey(false);
                else if (m.WParam == IdPreviousMaximized) SwitchFromKey(true);
            }
            catch (Exception ex) { EndWheelSelection(); log.Write($"ウィンドウ操作のキーの処理に失敗: {ex.Message}"); }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        EndWheelSelection();
        wheelTimer.Dispose();
        switcher.Dispose();
        if (Handle == 0) return;
        UnregisterHotKey(Handle, IdMaximize); UnregisterHotKey(Handle, IdNextMaximized);
        UnregisterHotKey(Handle, IdPreviousMaximized);
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
}
