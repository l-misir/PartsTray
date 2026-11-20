// Sony のヘッドホンと直接やり取りするメッセージ（有志の解析で「MDR」と呼ばれる方式）を組み立て・読み解きします。
// メッセージの形や電池の返事の読み方を変えるときはここを編集します。OS にはアクセスしない純粋な関数です。
namespace PartsTray.Core;

// 形は Gadgetbridge（Android の公開ソフト）の解析に合わせた。11/20 実機（WH-1000XM6）で確かめた：
//   初期化 0C 00 [00 00] → 返事の中身 01 00 03 00 30 32 00 00（8 バイト＝新しい方の方式 V2）
//   電池を聞く 0C [22 00] → 返事 23 00 64 03（ケーブルあり・100%）、23 00 64 00（ケーブルなし・100%）
public static class SonyMdr
{
    // Bluetooth の通り道（RFCOMM）のサービス。新しい機種は V2、古い機種は V1。
    public static readonly Guid ServiceV2 = new("956C7B26-D49A-4BA8-B03F-B17D393CB6E2");
    public static readonly Guid ServiceV1 = new("96CC203E-5068-46AD-B32D-E316F5E069BA");
    public const byte Ack = 0x01, Command = 0x0C;
    private const byte Start = 0x3E, End = 0x3C, Escape = 0x3D;

    public sealed record Message(byte Type, byte Seq, byte[] Payload);

    // [3E] 種類 番号 長さ(4 バイト、大きい桁から) 中身 合計 [3C]。3C・3D・3E は 3D と (値 & EF) の 2 バイトに置き換える。
    public static byte[] Encode(byte type, byte seq, ReadOnlySpan<byte> payload)
    {
        var body = new List<byte> { type, seq, (byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length };
        body.AddRange(payload.ToArray());
        byte sum = 0; foreach (byte b in body) sum += b;
        body.Add(sum);
        var frame = new List<byte> { Start };
        foreach (byte b in body) { if (b is Start or End or Escape) { frame.Add(Escape); frame.Add((byte)(b & 0xEF)); } else frame.Add(b); }
        frame.Add(End);
        return frame.ToArray();
    }

    // 受け取った分（buffer）からそろったメッセージを取り出す（取り出した分は buffer から消す）。合計が合わないものは捨てる。
    public static List<Message> Decode(List<byte> buffer)
    {
        var result = new List<Message>();
        while (true)
        {
            int start = buffer.IndexOf(Start);
            if (start < 0) { buffer.Clear(); break; }
            int end = buffer.IndexOf(End, start + 1);
            if (end < 0) { buffer.RemoveRange(0, start); break; }
            var m = new List<byte>();
            for (int i = start + 1; i < end; i++) m.Add(buffer[i] == Escape && i + 1 < end ? (byte)(buffer[++i] | 0x10) : buffer[i]);
            buffer.RemoveRange(0, end + 1);
            if (m.Count < 7) continue;
            int length = m[2] << 24 | m[3] << 16 | m[4] << 8 | m[5];
            if (length < 0 || 6 + length + 1 != m.Count) continue;
            byte sum = 0; for (int i = 0; i < m.Count - 1; i++) sum += m[i];
            if (sum != m[^1]) continue;
            result.Add(new Message(m[0], m[1], m.Skip(6).Take(length).ToArray()));
        }
        return result;
    }

    // 受け取ったコマンドへの返事（ACK）。番号は 1 - 受け取った番号。
    public static byte[] AckFor(Message received) => Encode(Ack, (byte)(1 - received.Seq), []);

    public static byte[] InitRequest() => Encode(Command, 0, [0x00, 0x00]);

    // ヘッドホン 1 つ分の電池（0x00）を聞く。V2 は 0x22、V1 は 0x10。
    public static byte[] BatteryRequest(bool v2, byte seq) => Encode(Command, seq, [v2 ? (byte)0x22 : (byte)0x10, 0x00]);

    // 電池の返事・知らせ（V2：0x23・0x25、V1：0x11・0x13）。[種類, 0x00（1 つ分）, 残量, 充電の状態]。
    // 充電の状態：0 = 充電していない、1・2 = 充電中（11/20 時点では解析の資料による。実機では未確認）、3 = ケーブルにつながって満タン（実機で確認）。
    public static BatteryReading? Battery(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4 || payload[0] is not (0x23 or 0x25 or 0x11 or 0x13) || payload[1] != 0x00 || payload[2] > 100) return null;
        var charge = payload[3] switch
        {
            0 => BatteryCharge.Discharging, 1 or 2 => BatteryCharge.Charging, 3 => BatteryCharge.Full, _ => BatteryCharge.Unknown
        };
        return new BatteryReading(payload[2], BatteryLevel.Unknown, charge, payload[3] is 1 or 2 or 3, false);
    }

    // 初期化の返事（中身の先頭が 0x01）。中身が 8 バイト以上なら V2。
    public static bool IsInitReply(Message m, out bool v2)
    {
        v2 = m.Payload.Length >= 8;
        return m.Type == Command && m.Payload.Length >= 4 && m.Payload[0] == 0x01;
    }
}
