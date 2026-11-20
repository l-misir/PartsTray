using System.Globalization;
using LogiGap.Core;
using Xunit;

namespace LogiGap.Tests;

public sealed class WheelTraceTests
{
    [Theory]
    [InlineData(0, 0x4C474150, WheelTraceSource.LogiGap)]
    [InlineData(1, 0x4C474150, WheelTraceSource.LogiGap)]
    [InlineData(2, 0x4C474150, WheelTraceSource.LogiGap)]
    [InlineData(3, 0x4C474150, WheelTraceSource.LogiGap)]
    [InlineData(1, 0, WheelTraceSource.Injected)]
    [InlineData(2, 0, WheelTraceSource.Injected)]
    [InlineData(3, 123, WheelTraceSource.Injected)]
    [InlineData(0, 0, WheelTraceSource.Device)]
    [InlineData(4, 123, WheelTraceSource.Device)]
    public void ClassifiesSource(uint flags, uint extra, WheelTraceSource expected) =>
        Assert.Equal(expected, WheelTrace.Source(flags, extra));

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void FormatAndHeaderAreCultureIndependent(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var time = new DateTime(2026, 11, 19, 17, 10, 41, 234);
            Assert.Equal("# 横スクロール記録 開始 2026-11-19 17:10:41.234 列: 時刻 前からの間隔ms 量 送り元 flags extra 横取り（縦ホイールは行末に wheel=V）", WheelTrace.Header(time));
            Assert.Equal("17:10:41.234 +8.1 -120 Injected flags=01 extra=0 taken=None wheel=V",
                WheelTrace.Format(new(123, -120, 1, 0, WheelTraceTaken.None, Vertical: true), time, 8.1));
            Assert.Equal("17:10:41.234 +8.1 -120 LogiGap flags=01 extra=4C474150 taken=Bridge",
                WheelTrace.Format(new(123, -120, 1, KeyStroke.ExtraInfo, WheelTraceTaken.Bridge), time, 8.1));
            Assert.Equal("17:10:41.234 +8.1 -120 Device flags=00 extra=0 taken=Bridge",
                WheelTrace.Format(new(123, -120, 0, 0, WheelTraceTaken.Bridge), time, 8.1));
            Assert.Equal("17:10:41.234 +0.0 120 Injected flags=02 extra=A taken=None",
                WheelTrace.Format(new(123, 120, 2, 10, WheelTraceTaken.None), time, 0));
            Assert.EndsWith("taken=Gesture", WheelTrace.Format(new(123, 1, 0, 0, WheelTraceTaken.Gesture), time, 1));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
