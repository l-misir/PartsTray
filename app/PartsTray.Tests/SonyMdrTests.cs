// Sony のヘッドホンとのメッセージ（SonyMdr.cs）の組み立て・読み解きを、11/20 に WH-1000XM6 で記録した実際のバイト列で検証します。
// メッセージの形や電池の読み方を変えたときに実行する純粋なテストです。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class SonyMdrTests
{
    [Fact]
    public void RequestsMatchTheBytesSentToTheRealHeadphones()
    {
        Assert.Equal("3E0C000000000200000E3C", Convert.ToHexString(SonyMdr.InitRequest()));
        Assert.Equal("3E010000000000013C", Convert.ToHexString(SonyMdr.AckFor(new SonyMdr.Message(0x0C, 1, []))));
        Assert.Equal("3E010100000000023C", Convert.ToHexString(SonyMdr.AckFor(new SonyMdr.Message(0x0C, 0, []))));
    }

    [Fact]
    public void DecodesFramesSplitAcrossReadsAndKeepsTheRest()
    {
        var frame = SonyMdr.Encode(0x0C, 1, [0x23, 0x00, 0x64, 0x03]);
        var buffer = new List<byte>(frame.Take(5));
        Assert.Empty(SonyMdr.Decode(buffer));
        buffer.AddRange(frame.Skip(5)); buffer.AddRange(SonyMdr.Encode(0x01, 0, []).Take(3));
        var m = Assert.Single(SonyMdr.Decode(buffer));
        Assert.Equal((byte)0x0C, m.Type); Assert.Equal((byte)1, m.Seq); Assert.Equal("23006403", Convert.ToHexString(m.Payload));
        Assert.Equal(3, buffer.Count);   // 次のメッセージの途中は残す
    }

    [Fact]
    public void SpecialBytesAreEscapedAndRestored()
    {
        byte[] payload = [0x3C, 0x3D, 0x3E, 0x10];
        var frame = SonyMdr.Encode(0x0C, 0, payload);
        Assert.DoesNotContain((byte)0x3C, frame.Skip(1).SkipLast(1));
        var m = Assert.Single(SonyMdr.Decode(new List<byte>(frame)));
        Assert.Equal(payload, m.Payload);
    }

    [Fact]
    public void BrokenChecksumIsDropped()
    {
        var frame = SonyMdr.Encode(0x0C, 1, [0x23, 0x00, 0x64, 0x00]);
        frame[^2] ^= 0x01;
        Assert.Empty(SonyMdr.Decode(new List<byte>(frame)));
    }

    [Theory]
    [InlineData("23006403", 100, BatteryCharge.Full, true)]          // ケーブルあり・満タン（実機）
    [InlineData("23006400", 100, BatteryCharge.Discharging, false)]  // ケーブルなし（実機）
    [InlineData("25004101", 65, BatteryCharge.Charging, true)]       // 知らせ（0x25）・充電中
    [InlineData("11003200", 50, BatteryCharge.Discharging, false)]   // 古い方式（V1）
    public void BatteryReplies(string hex, int percent, BatteryCharge charge, bool power)
    {
        var r = SonyMdr.Battery(Convert.FromHexString(hex))!;
        Assert.Equal(percent, r.Percent); Assert.Equal(charge, r.Charge); Assert.Equal(power, r.ExternalPower);
        Assert.False(r.Approximate);
    }

    [Theory]
    [InlineData("23016403")]   // 左右別（1 つ分ではない）
    [InlineData("230065")]     // 短い
    [InlineData("2300C800")]   // 100 を超える
    [InlineData("01000300")]   // 電池の返事ではない
    public void OtherPayloadsAreNotBattery(string hex) => Assert.Null(SonyMdr.Battery(Convert.FromHexString(hex)));

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"battery\":{\"sonyDirect\":false}}", false)]
    public void SonyDirectSettingDefaultsOn(string json, bool expected)
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        s.Validate(); Assert.Equal(expected, s.Battery.SonyDirect);
    }

    [Theory]
    [InlineData(4)] [InlineData(601)]
    public void SonyPollSecondsOutOfRangeIsRejected(int seconds)
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>($"{{\"battery\":{{\"sonyPollSeconds\":{seconds}}}}}", AppSettings.JsonOptions)!;
        Assert.Throws<InvalidDataException>(s.Validate);
    }

    [Fact]
    public void InitReplyTellsTheProtocolVersion()
    {
        Assert.True(SonyMdr.IsInitReply(new SonyMdr.Message(0x0C, 0, Convert.FromHexString("0100030030320000")), out bool v2));
        Assert.True(v2);
        Assert.True(SonyMdr.IsInitReply(new SonyMdr.Message(0x0C, 0, Convert.FromHexString("01004010")), out v2));
        Assert.False(v2);
        Assert.False(SonyMdr.IsInitReply(new SonyMdr.Message(0x0C, 0, Convert.FromHexString("F5100100")), out _));
    }
}
