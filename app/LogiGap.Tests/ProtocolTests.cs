using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData("100241100242B0", ReportKind.Connection, 0, 0)]
    [InlineData("1102143500089800000000000000000000000000", ReportKind.Response, 3, 5)]
    [InlineData("11020D0000560000000000000000000000000000", ReportKind.Notification, 0, 0)]
    [InlineData("1102FF1435080000000000000000000000000000", ReportKind.Error20, 3, 5)]
    [InlineData("10028F14350900", ReportKind.Error10, 3, 5)]
    public void ClassifiesWireFormats(string hex, ReportKind kind, byte fn, byte swid)
    {
        var report = HidReport.Parse(Convert.FromHexString(hex));
        Assert.NotNull(report); Assert.Equal(kind, report.Kind); Assert.Equal(fn, report.Function); Assert.Equal(swid, report.Swid);
        if (kind == ReportKind.Connection) { Assert.Equal(0xB042, report.Wpid); Assert.True(report.LinkUp); }
        if (kind is ReportKind.Error10 or ReportKind.Error20) Assert.Equal(20, report.Feature);
    }
    [Fact] public void RejectsTruncationAndInvalidTargets()
    {
        Assert.Null(HidReport.Parse([0x11, 2, 20, 0x35, 0, 8, 0x98]));
        Assert.Null(HidReport.Parse([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => HidReport.Request(255, 0, 1, []));
        Assert.Equal(7, HidReport.Request(2, 0, 1, [0, 0, 0x5A], true).Length);
    }
    [Fact] public void WheelScalingAndFractionalAccumulation()
    {
        var c = new ThumbWheelConverter();
        Assert.Equal(120, Enumerable.Range(0, 12).Sum(_ => c.Convert(1, 50, 1)));
        c.Reset(); Assert.Equal(0, c.Convert(1, 2, 1)); Assert.Equal(0, c.Convert(1, 2, 1)); Assert.Equal(1, c.Convert(1, 2, 1));
        Assert.Equal(-1, c.Convert(-3, 2, 1)); c.Reset(); Assert.Equal(-10, c.Convert(1, 50, -1));
    }
    [Fact] public void KeySequencesAreBalancedAndExtended()
    {
        Assert.Equal(new ushort[] { 0x11, 0x43, 0x43, 0x11 }, InputSequence.ForKeys("Ctrl+C").Select(k => k.VirtualKey));
        Assert.Equal(new uint[] { 0, 1, 3, 2 }, InputSequence.ForKeys("Win+Up").Select(k => k.Flags));
        Assert.Equal((ushort)0x5B, InputSequence.ForKeys("Win+Up")[0].VirtualKey);
        Assert.Equal(new uint[] { 0, 2 }, InputSequence.ForKeys("PrintScreen").Select(k => k.Flags));
        Assert.Equal((nuint)0x4C474150, KeyStroke.ExtraInfo);
    }
    [Fact] public void NativeWheelScalingTruncatesAndResetsFraction()
    {
        var c = new NativeWheelScaler();
        Assert.Equal(0, c.Convert(1, 50, 1));
        Assert.Equal(-1, c.Convert(1, 50, 1));
        Assert.Equal(0, c.Convert(1, 50, 1));
        c.Reset();
        Assert.Equal(0, c.Convert(-1, 50, 1));
        Assert.Equal(1, c.Convert(-1, 50, 1));
        Assert.Equal(60, c.Convert(120, 50, -1));
        Assert.Equal(0, c.Convert(120, 0, 1));
        Assert.Equal(-120, c.Convert(120, 100, 1));
    }
    [Fact] public void SettingsRejectUnsafeIdentifiersAndActions()
    {
        Assert.Throws<InvalidDataException>(() => new AppSettings { GapFiller = new() { OwnSwid = 14 } }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { GapFiller = new() { Buttons = new() { ["0x00C4"] = new() } } }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { GapFiller = new() { Dpi = new() { Primary = 2201 } } }.Validate());
        new AppSettings().Validate();
    }
}
