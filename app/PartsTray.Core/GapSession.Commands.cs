// 肩代わりの適用順序と、ボタン操作・引き継ぎ後の処理を選びます。
// 対象ボタン・動作・適用順序を変えるときに開きます。
namespace PartsTray.Core;

public sealed partial class GapSession
{
    private Command? NextCommand()
    {
        if (paused) return ListenWhilePaused ? NextButtonListenerCommand() : null;
        if (device != 0 && (state == SessionState.Active || startupCheck) && !blocked)
        {
            if (discoveryStep < FeatureOrder.Length)
            {
                ushort id = FeatureOrder[discoveryStep];
                return Read(0, 0, HidReport.Be(id), $"ROOT.getFeature {id:X4}", r =>
                {
                    if (r.Parameters[0] == 0 || r.Parameters[0] >= 0x80 || features.Values.Contains(r.Parameters[0]))
                        throw new InvalidDataException($"必須機能が無い/不正: {id:X4}");
                    features[id] = r.Parameters[0]; discoveryStep++;
                    foreach (var seen in earlyOptions) ObserveOptions(seen);
                });
            }
            if (startupCheck)
                return Read(features[0x1B04], 2, HidReport.Be(0x52), "接続判定 getCidReporting", r =>
                {
                    bool running = optionsRunning(); // Query at the decision, not at receiver open.
                    bool divert = (r.Parameters[2] & 1) != 0;
                    bool fresh = false;
                    DateTime? started = null;
                    string optionsStartLog = "";
                    if (divert && running && !inheritedActive && optionsStartedUtc != null)
                    {
                        try
                        {
                            started = optionsStartedUtc();
                            fresh = started is { } s && s > createdUtc &&
                                (lastOptionsConfigUtc is not { } seen || seen < s);
                        }
                        catch { started = null; fresh = false; }
                        // 要確認: 起動時刻が不明なら「起動後」の観測を確認できないため「なし」と記録し、判定は従来どおり撤退する。
                        bool configured = started is { } start && lastOptionsConfigUtc is { } seenAt && seenAt >= start;
                        string startTime = started?.ToLocalTime().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) ?? "不明";
                        optionsStartLog = $" Options+起動={startTime} 起動後の設定={(configured ? "あり" : "なし")}";
                    }
                    startupCheck = false;
                    log.Write($"接続判定: divert={(r.Parameters[2] & 1)} Options+={running} previousActive={inheritedActive}{optionsStartLog}");
                    if (divert && running && !inheritedActive && !fresh)
                    { cleanupDone = true; Change(SessionState.Retreated, "接続判定: Options+ 設定済み"); }
                    else
                    {
                        Change(SessionState.Active, fresh
                            ? "接続判定: Options+ は起動後まだ設定していない（前の横取りが残っている）"
                            : "接続判定: 設定の肩代わりが必要");
                        // 要確認: 通知の秒数は OS の表示言語によらず小数点と 2 桁を固定する。
                        NotifySwitch(FormattableString.Invariant($"肩代わりを開始しました（接続から {(activeAt - linkedAt).TotalSeconds:F2} 秒）"));
                    }
                });
            if (applyStep == 0)
                return Write(features[0x2201], 3, DpiBytes(sessionSettings.Dpi.Primary), "setSensorDpi 初期適用", _ =>
                { applyStep++; }, submitted: () => log.Write($"適用開始 link+{(clock.Now - linkedAt).TotalMilliseconds:F3}ms"));
            if (applyStep <= ButtonOrder.Count)
            {
                ushort cid = ButtonOrder[applyStep - 1];
                if (!sessionSettings.Buttons.ContainsKey($"0x{cid:X4}")) { applyStep++; return NextCommand(); }
                return Write(features[0x1B04], 3, CidBytes(cid, 3), $"setCidReporting ON {cid:X4}", _ => applyStep++, submitted: () => owned.Add(cid));
            }
            if (applyStep == ButtonOrder.Count + 1)
            {
                if (sessionSettings.ThumbWheel.Mode == "off") { applyStep = ButtonOrder.Count + 3; CompleteApply(); return NextCommand(); }
                if (sessionSettings.ThumbWheel.Mode == "hook")
                    return Read(features[0x2150], 1, [], "THUMB_WHEEL getStatus 残りの横取りを確認", r =>
                    {
                        var p = r.Parameters;
                        if (p.Length < 2)
                        {
                            // 要確認: 2 バイト未満の応答は横取りなしとして扱い、状態が不明なまま解除を書き込まない。
                            log.Write("サムホイール: 横取りは残っていない（応答が 2 バイト未満）");
                            applyStep = ButtonOrder.Count + 3; CompleteApply();
                        }
                        else if ((p[0] & 1) != 0)
                        {
                            log.Write($"サムホイール: 横取りが残っていたので解除する（{p[0]:X2} {p[1]:X2}）");
                            applyStep = ButtonOrder.Count + 2;
                        }
                        else
                        {
                            log.Write($"サムホイール: 横取りは残っていない（{p[0]:X2} {p[1]:X2}）");
                            applyStep = ButtonOrder.Count + 3; CompleteApply();
                        }
                    });
                // 要確認: setReporting(mode,invert)=(1,1) は推定。直後に getStatus を検証する。
                return Write(features[0x2150], 2, [1, 1], "THUMB_WHEEL setReporting ON", _ => applyStep++, submitted: () => thumbOwned = true);
            }
            if (applyStep == ButtonOrder.Count + 2)
            {
                if (sessionSettings.ThumbWheel.Mode == "hook")
                    return Write(features[0x2150], 2, [0, 0], "THUMB_WHEEL 残りの横取りを解除", _ =>
                    { applyStep = ButtonOrder.Count + 3; CompleteApply(); });
                return Read(features[0x2150], 1, [], "THUMB_WHEEL getStatus 読み戻し", r =>
                {
                    var p = r.Parameters;
                    log.Write(p[0] == 1 && (p[1] & 1) == 1
                        ? $"サムホイール読み戻し OK {p[0]:X2} {p[1]:X2}"
                        : $"サムホイール読み戻し不一致 {p[0]:X2} {p[1]:X2}: 要確認");
                    applyStep++; CompleteApply();
                });
            }
        }
        while (actions.TryDequeue(out var a))
        {
            if (a.Generation != generation || state != SessionState.Active) continue;
            if (a.Action == null) { input.HorizontalWheel(a.Delta); continue; }
            log.Write($"ボタン CID={a.Cid:X4} 操作={a.Action.Action} {a.Action.Keys}");
            if (a.Action.Action == "ToggleDpi")
            {
                dpi = dpi == sessionSettings.Dpi.Primary ? sessionSettings.Dpi.Secondary : sessionSettings.Dpi.Primary;
                return Write(features[0x2201], 3, DpiBytes(dpi), $"setSensorDpi ボタン {dpi}", _ => { });
            }
            if (a.Action.Action is "Maximize" or "NextMaximized" or "PreviousMaximized")
            {
                // 本当の最大化・切り替えは、設定の windowKeys のキーを送り、WindowKeysModule の登録したキーとして動かす。
                string keys = a.Action.Action switch
                {
                    "Maximize" => windowKeys.Maximize,
                    "PreviousMaximized" => windowKeys.PreviousMaximized,
                    _ => windowKeys.NextMaximized
                };
                if (string.IsNullOrWhiteSpace(keys)) log.Write("ウィンドウ操作のキーが空のため、何もしません（設定の「ウィンドウ・ホイール」でキーを決めてください）");
                else input.Keys(keys);
                continue;
            }
            if (a.Action.Action == "Hold") continue; // 押下通知だけを使い、キーや DPI は送らない。
            input.Keys(a.Action.Keys!);
        }
        if (state == SessionState.Retreated && !startupCheck && !cleanupDone && clock.Now >= retreatAt + TimeSpan.FromSeconds(sessionSettings.CleanupDelaySeconds))
            PrepareCleanup();
        while (cleanup.TryDequeue(out var c)) if (c.Allowed()) return c;
        return device == 0 ? NextProbe() : null;
    }
}
