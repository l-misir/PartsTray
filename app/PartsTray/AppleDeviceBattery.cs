// USB でつないだ iPhone・iPad の電池を、Apple のサービス（Apple Mobile Device Service）経由で読みます。
// 読み直す間隔やつなぎ方を変えるときはここを編集します。やり取りの形（plist）は PartsTray.Core/ApplePlist.cs。
using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PartsTray.Core;

namespace PartsTray;

// Windows は iPhone・iPad の電池の値を持っていない（11/20 実機：機器のまとまりの情報にも、写真のやり取りの仕組み（WPD）にもなかった）。
// iTunes や「Apple デバイス」アプリと一緒に入る Apple Mobile Device Service は、127.0.0.1:27015 で USB の iPhone・iPad との
// やり取り（usbmux）を受け付けている。そこから機器の lockdownd（62078 番）につなぎ、電池の値（com.apple.mobile.battery）を聞く。
//   1. usbmux：ListDevices（つながっている機器）→ ReadPairRecord（この PC を「信頼」したときの情報をサービスから借りる）→ Connect
//   2. lockdownd：StartSession → 暗号化（借りた証明書で TLS）→ GetValue（機器の種類・名前・電池）→ StopSession
// この PC を信頼していない機器や、サービスが入っていない PC では何もしない。借りた情報はこのやり取りにだけ使い、保存も記録もしない。
// 11/20 実機：iPhone SE（第 3 世代）97%・充電中、iPad 87%・充電中 と読めた。
internal sealed class AppleDeviceBattery : IDisposable
{
    private const int MuxPort = 27015, LockdownPort = 62078;
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(30), Limit = TimeSpan.FromSeconds(10);
    private readonly ILog log;
    private readonly System.Threading.Timer timer;
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);
    private int reading;
    private volatile bool disposed, enabled;
    private Thread? listener;

    // 読み終えたら、そのとき USB でつながっている iPhone・iPad の一覧を知らせる（別のスレッドから）。
    public event Action<IReadOnlyList<BatteryDevice>>? Read;

    public AppleDeviceBattery(ILog log)
    {
        this.log = log;
        timer = new(_ => ReadNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value;
        if (value)
        {
            timer.Change(0, (int)Every.TotalMilliseconds);
            listener ??= new Thread(Listen) { IsBackground = true, Name = "PartsTray Apple devices" };
            if (!listener.IsAlive) listener.Start();
        }
        else { timer.Change(Timeout.Infinite, Timeout.Infinite); Read?.Invoke([]); }
    }

    // つないだ・外したときに、すぐ読み直す（usbmux の Listen で知らせを受け取る）。サービスがないときは 1 分ごとに試し直す。
    private void Listen()
    {
        while (!disposed)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect("127.0.0.1", MuxPort);
                var stream = client.GetStream();
                SendMux(stream, new() { ["MessageType"] = "Listen", ["ClientVersionString"] = "PartsTray", ["ProgName"] = "PartsTray" });
                while (!disposed)
                {
                    var message = ReceiveMux(stream);
                    if (enabled && message.GetValueOrDefault("MessageType") is "Attached" or "Detached")
                        timer.Change(2000, (int)Every.TotalMilliseconds);   // つないだ直後は機器の準備に少しかかる
                }
            }
            catch (Exception) when (!disposed) { Thread.Sleep(60000); }
            catch (Exception) { return; }
        }
    }

    private void ReadNow()
    {
        if (disposed || !enabled || Interlocked.Exchange(ref reading, 1) != 0) return;
        try
        {
            var result = new List<BatteryDevice>();
            Dictionary<string, object?> list;
            try { list = Mux(new() { ["MessageType"] = "ListDevices", ["ClientVersionString"] = "PartsTray", ["ProgName"] = "PartsTray" }); }
            catch (SocketException) { Read?.Invoke(result); return; }   // Apple のサービスが入っていない・止まっている
            catch (Exception ex)
            {
                // 11/21：スリープからの復帰などで接続が途中で切れると IOException などになる。ここで受け止めないと、
                // タイマーの中の例外でアプリごと落ちる（夜中に PartsTray が消えていた件）。つながっていないものとして扱う。
                log.Write($"Apple のサービスとのやり取りに失敗: {ex.GetType().Name}");
                Read?.Invoke(result); return;
            }
            foreach (var item in (list.GetValueOrDefault("DeviceList") as List<object?> ?? []).OfType<Dictionary<string, object?>>())
            {
                if (item.GetValueOrDefault("Properties") is not Dictionary<string, object?> props || props.GetValueOrDefault("ConnectionType") as string != "USB") continue;
                if (props.GetValueOrDefault("SerialNumber") is not string udid || item.GetValueOrDefault("DeviceID") is not long id) continue;
                try { if (ReadDevice(id, udid) is { } device) result.Add(device); }
                catch (Exception ex) { log.Write($"iPhone・iPad の電池を読めません: {ex.GetType().Name}"); }
            }
            Read?.Invoke(result);
        }
        finally { Volatile.Write(ref reading, 0); }
    }

    private BatteryDevice? ReadDevice(long id, string udid)
    {
        var pairReply = Mux(new() { ["MessageType"] = "ReadPairRecord", ["PairRecordID"] = udid, ["ClientVersionString"] = "PartsTray", ["ProgName"] = "PartsTray" });
        if (pairReply.GetValueOrDefault("PairRecordData") is not byte[] pairData) return null;   // この PC を信頼していない
        var pair = ApplePlist.Read(pairData);
        using var client = new TcpClient();
        client.ReceiveTimeout = client.SendTimeout = (int)Limit.TotalMilliseconds;
        client.Connect("127.0.0.1", MuxPort);
        var stream = client.GetStream();
        var connect = MuxOn(stream, new() { ["MessageType"] = "Connect", ["DeviceID"] = id, ["PortNumber"] = (long)BinaryPrimitives.ReverseEndianness((ushort)LockdownPort), ["ClientVersionString"] = "PartsTray", ["ProgName"] = "PartsTray" });
        if (connect.GetValueOrDefault("Number") is not 0L) return null;
        Stream s = stream;
        var start = Lockdown(s, new() { ["Label"] = "PartsTray", ["Request"] = "StartSession", ["HostID"] = pair.GetValueOrDefault("HostID"), ["SystemBUID"] = pair.GetValueOrDefault("SystemBUID") });
        if (start.ContainsKey("Error")) return null;   // ロック中で信頼の確認ができない、など
        if (start.GetValueOrDefault("EnableSessionSSL") is true)
        {
            using var pem = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString((byte[])pair["HostCertificate"]!), Encoding.ASCII.GetString((byte[])pair["HostPrivateKey"]!));
            var certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
            var ssl = new SslStream(stream, false, (_, _, _, _) => true);   // 機器の証明書は自分で作ったものなので確かめない
            ssl.AuthenticateAsClient(new SslClientAuthenticationOptions { TargetHost = "device", ClientCertificates = [certificate], CertificateRevocationCheckMode = X509RevocationMode.NoCheck });
            s = ssl;
        }
        var battery = Lockdown(s, new() { ["Label"] = "PartsTray", ["Request"] = "GetValue", ["Domain"] = "com.apple.mobile.battery" });
        string deviceClass = Lockdown(s, new() { ["Label"] = "PartsTray", ["Request"] = "GetValue", ["Key"] = "DeviceClass" }).GetValueOrDefault("Value") as string ?? "";
        string name = Lockdown(s, new() { ["Label"] = "PartsTray", ["Request"] = "GetValue", ["Key"] = "DeviceName" }).GetValueOrDefault("Value") as string ?? deviceClass;
        try { Lockdown(s, new() { ["Label"] = "PartsTray", ["Request"] = "StopSession", ["SessionID"] = start.GetValueOrDefault("SessionID") }); } catch (Exception) { }
        if (battery.GetValueOrDefault("Value") is not Dictionary<string, object?> value || ApplePlist.Battery(value) is not { } reading) return null;
        var kind = deviceClass == "iPad" ? BatteryDeviceKind.Tablet : BatteryDeviceKind.Phone;
        if (logged.Add(udid)) log.Write($"{deviceClass} の電池を Apple のサービス経由で読めました（{reading.Percent}%・{reading.Charge}）");
        return new BatteryDevice($"apple:{udid}", string.IsNullOrWhiteSpace(name) ? deviceClass : name, kind, true, reading, DateTime.UtcNow);
    }

    // usbmux：16 バイトの頭（全体の長さ・版 1・種類 8＝plist・番号、どれも小さい桁から）＋ XML の plist。
    private static Dictionary<string, object?> Mux(Dictionary<string, object?> message)
    {
        using var client = new TcpClient();
        client.ReceiveTimeout = client.SendTimeout = (int)Limit.TotalMilliseconds;
        client.Connect("127.0.0.1", MuxPort);
        return MuxOn(client.GetStream(), message);
    }
    private static Dictionary<string, object?> MuxOn(NetworkStream stream, Dictionary<string, object?> message) { SendMux(stream, message); return ReceiveMux(stream); }
    private static void SendMux(NetworkStream stream, Dictionary<string, object?> message)
    {
        byte[] body = Encoding.UTF8.GetBytes(ApplePlist.Write(message));
        var head = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(head, (uint)(16 + body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), 1);
        stream.Write(head); stream.Write(body);
    }
    private static Dictionary<string, object?> ReceiveMux(NetworkStream stream)
    {
        var head = ReadExact(stream, 16);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(head);
        if (length < 16 || length > 1 << 20) throw new InvalidDataException("usbmux の長さが不正です");
        return ApplePlist.Read(ReadExact(stream, (int)length - 16));
    }
    // lockdownd：4 バイトの長さ（大きい桁から）＋ XML の plist。
    private static Dictionary<string, object?> Lockdown(Stream stream, Dictionary<string, object?> message)
    {
        byte[] body = Encoding.UTF8.GetBytes(ApplePlist.Write(message));
        var length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        stream.Write(length); stream.Write(body); stream.Flush();
        uint size = BinaryPrimitives.ReadUInt32BigEndian(ReadExact(stream, 4));
        if (size > 1 << 20) throw new InvalidDataException("lockdownd の長さが不正です");
        return ApplePlist.Read(ReadExact(stream, (int)size));
    }
    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count]; int read = 0;
        while (read < count) { int n = stream.Read(buffer, read, count - read); if (n <= 0) throw new IOException("切断されました"); read += n; }
        return buffer;
    }

    public void Dispose() { disposed = true; enabled = false; timer.Dispose(); }
}
