// 肩代わりが無効でも、holdButton を設定した場合だけボタン通知用の機能番号を読み取ります。
// 通知を受ける条件を変えるときに開きます。横取り・DPI・入力送信は行いません。
namespace PartsTray.Core;

public sealed partial class GapSession
{
    private bool ListenWhilePaused => paused && windowKeys.HoldButton.Length != 0;

    private Command? NextButtonListenerCommand()
    {
        if (device == 0) return NextProbe();
        if (blocked) return null;
        foreach (ushort id in new ushort[] { 0x1B04, 0x2150 })
        {
            if (features.ContainsKey(id)) continue;
            return Read(0, 0, HidReport.Be(id), $"ボタン通知 ROOT.getFeature {id:X4}", r =>
            {
                if (r.Parameters[0] is 0 or >= 0x80 || features.Values.Contains(r.Parameters[0]))
                    throw new InvalidDataException($"ボタン通知の機能番号が不正: {id:X4}");
                features[id] = r.Parameters[0];
            }, () => ListenWhilePaused && device != 0);
        }
        return null;
    }
}
