// 状態の切り替えと Options+・ボタン・ホイールの通知を処理します。
// 通知の対象や二重実行を防ぐ条件を変えるときに開きます。
using System.Buffers.Binary;

namespace PartsTray.Core;

public sealed partial class GapSession
{
    private void RememberSession()
    {
        if (device == 0) return;
        if (state == SessionState.Active) previousActive.Add((receiverIdentity, device));
        else previousActive.Remove((receiverIdentity, device));
    }
    private void Change(SessionState next, string reason)
    {
        var old = state; state = next;
        if (next == SessionState.Active)
        {
            activeAt = clock.Now;
            if (!paused && sessionSettings.ThumbWheel.Mode == "hook" && !bridgeActive)
            {
                nativeScaler.Reset();
                bridgeReceived = bridgeReceivedTotal = bridgeSent = bridgeSentTotal = 0;
                bridgeExamples.Clear(); bridgeFailureLogged = false;
                bridgeActive = true;
                log.Write($"サムホイール: フック方式で橋渡しを開始（向き反転・速度 {settings.ThumbWheel.SpeedPercent}%）");
            }
        }
        log.Write($"状態 {old} -> {next}: {reason} monotonic={clock.Now.TotalMilliseconds:F3}ms");
        StateChanged?.Invoke(next);
    }
    private void StopBridge(string reason)
    {
        if (!bridgeActive) return;
        bridgeActive = false;
        log.Write($"サムホイール: 橋渡しを終了（{reason}） 受け取り {bridgeReceived} 件（合計 {bridgeReceivedTotal}）、送信 {bridgeSent} 件（合計 {bridgeSentTotal}）、元の量の例 [{string.Join(",", bridgeExamples)}]");
        bridgeReceived = bridgeReceivedTotal = bridgeSent = bridgeSentTotal = 0;
        bridgeExamples.Clear(); nativeScaler.Reset();
        // 要確認: 終了後の残りキューも同じ橋渡しとして、送信失敗の記録済み状態は次の開始まで保持する。
    }
    private void NotifySwitch(string text)
    {
        if (!settings.NotifyOnSwitch) return;
        log.Write($"通知: {text}");
        SwitchNotice?.Invoke(text);
    }
    private bool IsFeature(HidReport r, ushort id) => features.TryGetValue(id, out var f) && r.Feature == f;
    private void ObserveOptions(HidReport r)
    {
        bool cidWrite = IsFeature(r, 0x1B04) && r.Function == 3;
        bool thumbWrite = IsFeature(r, 0x2150) && r.Function == 2;
        bool dpiWrite = IsFeature(r, 0x2201) && r.Function == 3;
        if (!cidWrite && !thumbWrite && !dpiWrite) return;
        lastOptionsConfigUtc = utcNow();
        if (cidWrite && r.Parameters.Length >= 3) optionsFlags[HidReport.U16(r.Parameters)] = r.Parameters[2];
        if (thumbWrite)
        {
            optionsThumb = true;
            StopBridge("Options+ がサムホイールを設定");
        }
        if (state == SessionState.Active || startupCheck)
        {
            bool wasActive = state == SessionState.Active;
            Invalidate(); startupCheck = false; retreatAt = clock.Now;
            Change(SessionState.Retreated, $"Options+ 設定開始（swid={r.Swid:X}）: {(cidWrite ? "setCidReporting" : thumbWrite ? "setReporting" : "setSensorDpi")}");
            if (wasActive)
                NotifySwitch(FormattableString.Invariant($"Options+ に引き継ぎました（肩代わり {(retreatAt - activeAt).TotalSeconds:F2} 秒）"));
        }
    }
    private void ObserveIgnoredInput(HidReport r)
    {
        string reason = startupCheck ? "接続判定中" : "Options+ に引き継ぎ済み";
        // 要確認: 不完全なボタン通知は既存の ObserveInput と同じく無視し、押下履歴も保持する。
        if (IsFeature(r, 0x1B04) && r.Function == 0 && r.Parameters.Length >= 8)
        {
            HashSet<ushort> now = [];
            for (int i = 0; i < 8; i += 2)
            {
                ushort cid = HidReport.U16(r.Parameters.AsSpan(i, 2));
                if (cid == 0 || !now.Add(cid) || heldButtons.Contains(cid)) continue;
                if (state != SessionState.Active && sessionSettings.Buttons.TryGetValue($"0x{cid:X4}", out var action))
                    log.Write($"ボタン CID={cid:X4} 押下: PartsTray は実行しない（{reason}） 設定={action.Action} {action.Keys}");
            }
            heldButtons = now;
            // 購読側の例外でも、自分が横取りしたボタンの処理を止めない。
            try { PressedButtonsChanged?.Invoke(Array.AsReadOnly(now.ToArray())); }
            catch (Exception ex) { log.Write($"ボタン押下一覧の通知に失敗: {ex.Message}"); }
        }
        if (state != SessionState.Active && IsFeature(r, 0x2150) && r.Function == 0 &&
            r.Parameters.Length >= 6 && BinaryPrimitives.ReadInt16BigEndian(r.Parameters) != 0)
        {
            ThumbWheelRotated?.Invoke(BinaryPrimitives.ReadInt16BigEndian(r.Parameters));
            if (!ignoredThumbLogged)
            {
                ignoredThumbLogged = true;
                log.Write($"サムホイール回転を検出: PartsTray は送信しない（{reason}）※このリンクでは 1 回だけ記録");
            }
        }
    }
    private void ObserveInput(HidReport r)
    {
        if (IsFeature(r, 0x1B04) && r.Function == 0 && r.Parameters.Length >= 8)
        {
            HashSet<ushort> now = [];
            for (int i = 0; i < 8; i += 2)
            {
                ushort cid = HidReport.U16(r.Parameters.AsSpan(i, 2));
                if (cid == 0 || !now.Add(cid) || pressed.Contains(cid) || !owned.Contains(cid)) continue;
                if (sessionSettings.Buttons.TryGetValue($"0x{cid:X4}", out var action))
                    actions.Enqueue((generation, cid, action, 0));
            }
            pressed = now;
        }
        if (IsFeature(r, 0x2150) && r.Function == 0 && thumbOwned && r.Parameters.Length >= 6)
        {
            // 要確認: 回転は先頭 int16 BE。ログの +36.999 秒等に +1 があるが方向・単位は実機で確認。
            short rotation = BinaryPrimitives.ReadInt16BigEndian(r.Parameters);
            int delta = wheel.Convert(rotation, settings.ThumbWheel.SpeedPercent, settings.ThumbWheel.DirectionSign);
            if (delta != 0) actions.Enqueue((generation, 0, null, delta));
        }
    }
}
