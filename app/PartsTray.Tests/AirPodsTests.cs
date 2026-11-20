// AirPods の電波（近くのペアリングの知らせ）の読み解きと、アイコン・説明文への変換を検証します。
// 読み解き方（AirPods.cs）を変えたときに実行する純粋なテストです。
using System.Text.Json;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class AirPodsTests
{
    // 会社 ID の後ろのバイト列：[0] 0x07 [1] 長さ [2] 0x01 [3..4] 型番 [5] 状態 [6] 左右の残量 [7] 充電中の印とケースの残量 ...
    private static byte[] Message(byte status, byte pods, byte chargeAndCase) =>
        [0x07, 0x19, 0x01, 0x19, 0x20, status, pods, chargeAndCase, 0x00, 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void NormalOrderReadsLeftFromLowNibble()
    {
        // 状態の上位 4 bit = 2（2 の位が 1）なので入れ替わりなし：左 = 下位 4 bit
        var r = AirPods.Parse(Message(0x2B, 0x89, 0x15))!;
        Assert.Equal((ushort)0x1920, r.Model);
        Assert.Equal(90, r.Left); Assert.Equal(80, r.Right); Assert.Equal(50, r.Case);
        Assert.True(r.LeftCharging); Assert.False(r.RightCharging); Assert.False(r.CaseCharging);
    }

    [Fact]
    public void FlippedOrderSwapsLeftAndRight()
    {
        // 状態の上位 4 bit = 0（2 の位が 0）なので入れ替わり：左 = 上位 4 bit、充電中の印も左右が逆
        var r = AirPods.Parse(Message(0x0B, 0x89, 0x65))!;
        Assert.Equal(80, r.Left); Assert.Equal(90, r.Right);
        Assert.True(r.LeftCharging); Assert.False(r.RightCharging); Assert.True(r.CaseCharging);
    }

    [Fact]
    public void UnknownLevelsAreNull()
    {
        var r = AirPods.Parse(Message(0x2B, 0xF7, 0x0F))!;
        Assert.Equal(70, r.Left); Assert.Null(r.Right); Assert.Null(r.Case);
    }

    [Theory]
    [InlineData(new byte[] { 0x10, 0x05, 0x01, 0x19, 0x20, 0x2B, 0x89, 0x15 })]   // 別の種類の知らせ
    [InlineData(new byte[] { 0x07, 0x19, 0x01, 0x19, 0x20, 0x2B, 0x89 })]          // 短すぎる
    [InlineData(new byte[] { })]
    public void OtherMessagesAreIgnored(byte[] data) => Assert.Null(AirPods.Parse(data));

    [Fact]
    public void IconUsesLowerPodAndTooltipShowsEachPart()
    {
        var reading = AirPods.ToReading(AirPods.Parse(Message(0x2B, 0x89, 0x15))!)!;
        Assert.Equal(80, reading.Percent);
        Assert.Equal(BatteryCharge.Charging, reading.Charge);
        Assert.True(reading.Approximate);
        Assert.Equal("左 90%（充電中）・右 80%・ケース 50%", reading.Detail);
        var device = new BatteryDevice("bluetooth:x", "AirPods", BatteryDeviceKind.Earbuds, true, reading, DateTime.UtcNow);
        Assert.Equal("AirPods 80%（充電中）（概算）\n左 90%（充電中）・右 80%・ケース 50%", BatteryMath.Tooltip(device));
    }

    [Fact]
    public void OnlyOnePodOrOnlyCaseStillGivesAValue()
    {
        Assert.Equal(70, AirPods.ToReading(AirPods.Parse(Message(0x2B, 0xF7, 0x05))!)!.Percent);
        Assert.Equal(50, AirPods.ToReading(AirPods.Parse(Message(0x2B, 0xFF, 0x05))!)!.Percent);
        Assert.Null(AirPods.ToReading(AirPods.Parse(Message(0x2B, 0xFF, 0x0F))!));
    }

    [Fact]
    public void FindMySuffixIsRemovedFromNames() => Assert.Equal("AirPods", BatteryMath.BluetoothName("AirPods - Find My"));

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"battery\":{\"airPods\":false}}", false)]
    public void AirPodsSettingDefaultsOn(string json, bool expected)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        s.Validate(); Assert.Equal(expected, s.Battery.AirPods);
    }
}
