// 接続済み機器の識別と、所有した横取りの後片付けを行います。
// 機器の識別方法や後片付け条件を変えるときに開きます。
namespace PartsTray.Core;

public sealed partial class GapSession
{
    private Command? NextProbe()
    {
        if (probe == null)
        {
            if (!probes.TryDequeue(out byte d)) return null;
            probe = new(d);
        }
        var candidate = probe;
        bool Allowed() => (!paused || ListenWhilePaused) && device == 0 && probe == candidate;
        Command ReadName(byte feature, byte fn, byte[] parameters, string name, Action<HidReport> success) =>
            new(candidate.Device, feature, fn, parameters, name, false, false, Allowed, success);
        if (!candidate.PingConfirmed)
            return ReadName(0, 1, [0, 0, 0x5A], $"起動 ping dev={candidate.Device}", r =>
            {
                if (r.Parameters.Length < 3 || r.Parameters[2] != 0x5A) { probe = null; return; }
                candidate.PingConfirmed = true;
                if (identities.GetValueOrDefault(candidate.Device) == 0xB042) Begin(candidate.Device, true);
            });
        if (candidate.NameFeature == 0)
            return ReadName(0, 0, [0, 5], "識別 ROOT.getFeature 0005", r =>
            {
                if (r.Parameters[0] is 0 or >= 0x80) { probe = null; return; }
                candidate.NameFeature = r.Parameters[0];
            });
        if (!candidate.NameLength.HasValue)
            return ReadName(candidate.NameFeature, 0, [], "DEVICE_NAME 長さ", r =>
            {
                if (r.Parameters[0] == 0) { probe = null; return; }
                candidate.NameLength = r.Parameters[0];
            });
        return ReadName(candidate.NameFeature, 1, [(byte)candidate.Name.Count], "DEVICE_NAME 本文", r =>
        {
            int count = Math.Min(candidate.NameLength.Value - candidate.Name.Count, r.Parameters.Length);
            candidate.Name.AddRange(r.Parameters.Take(count));
            if (candidate.Name.Count != candidate.NameLength.Value) return;
            if (candidate.Name.SequenceEqual("MX Master 4"u8.ToArray()))
            {
                identities[candidate.Device] = 0xB042;
                Begin(candidate.Device, true);
            }
            else
            {
                log.Write($"識別 dev={candidate.Device}: 対象外。これ以上の照会/書込みはしない");
                previousActive.Remove((receiverIdentity, candidate.Device));
                probe = null;
            }
        });
    }
    private void CompleteApply() => log.Write($"適用完了 link+{(clock.Now - linkedAt).TotalMilliseconds:F3}ms");
    private Command Read(byte f, byte fn, byte[] p, string name, Action<HidReport> success, Func<bool>? allowed = null, bool cleaning = false)
        => new(device, f, fn, p, name, false, cleaning, allowed ?? (() => !paused && device != 0 && (state == SessionState.Active || startupCheck)), success);
    private Command Write(byte f, byte fn, byte[] p, string name, Action<HidReport> success, bool cleaning = false, Func<bool>? allowed = null, Action? submitted = null)
        => new(device, f, fn, p, name, true, cleaning, allowed ?? (() => !paused && state == SessionState.Active), success, submitted);
    private static byte[] DpiBytes(int value) => [0, (byte)(value >> 8), (byte)value];
    private static byte[] CidBytes(ushort cid, byte flags) => [(byte)(cid >> 8), (byte)cid, flags, (byte)(cid >> 8), (byte)cid];
    private bool CidNeedsCleanup(ushort cid) => !optionsFlags.TryGetValue(cid, out byte flags) || (flags & 1) == 0;
    private void PrepareCleanup()
    {
        cleanupDone = true;
        int gen = generation;
        log.Write("後片付け開始（このセッションで 1 回のみ）");
        foreach (ushort cid in owned.ToArray())
        {
            bool Allowed() => !paused && generation == gen && state == SessionState.Retreated && CidNeedsCleanup(cid);
            if (!Allowed()) continue;
            cleanup.Enqueue(Read(features[0x1B04], 2, HidReport.Be(cid), $"後片付け getCidReporting {cid:X4}", r =>
            {
                if ((r.Parameters[2] & 1) != 0 && Allowed())
                    cleanup.Enqueue(Write(features[0x1B04], 3, CidBytes(cid, 2), $"後片付け OFF {cid:X4}", _ => { }, true, Allowed));
            }, Allowed, true));
        }
        bool ThumbAllowed() => !paused && generation == gen && state == SessionState.Retreated && thumbOwned && !optionsThumb;
        if (ThumbAllowed())
            cleanup.Enqueue(Read(features[0x2150], 1, [], "後片付け getThumbStatus", r =>
            {
                if ((r.Parameters[0] & 1) != 0 && ThumbAllowed())
                    cleanup.Enqueue(Write(features[0x2150], 2, [0, 0], "後片付け THUMB OFF", _ => { }, true, ThumbAllowed));
            }, ThumbAllowed, true));
    }
}
