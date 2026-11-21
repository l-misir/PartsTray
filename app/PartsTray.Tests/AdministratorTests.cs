// 管理者設定・タスク XML・管理者ウィンドウの候補選択を OS 操作なしで検証します。
// 管理者起動の既定値やタスクの実行条件を変えるときに開きます。
using System.Text.Json;
using System.Xml.Linq;
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class AdministratorTests
{
    [Fact]
    public void DefaultsRemainNormalPrivilegeAndDeviceWheelIsEnabled()
    {
        var settings = new AppSettings();
        Assert.False(settings.RunAsAdministrator);
        Assert.True(settings.WindowKeys.ThumbWheelFromDevice);
        settings.Validate();
    }

    [Theory]
    [InlineData("{}", false, true)]
    [InlineData("{\"runAsAdministrator\":true}", true, true)]
    [InlineData("{\"runAsAdministrator\":false,\"windowKeys\":{\"thumbWheelFromDevice\":false}}", false, false)]
    public void ReadsAndRoundTripsSettings(string json, bool administrator, bool deviceWheel)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions)!;
        settings.Validate();
        Assert.Equal(administrator, settings.RunAsAdministrator);
        Assert.Equal(deviceWheel, settings.WindowKeys.ThumbWheelFromDevice);
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, AppSettings.JsonOptions), AppSettings.JsonOptions)!;
        Assert.Equal(administrator, copy.RunAsAdministrator);
        Assert.Equal(deviceWheel, copy.WindowKeys.ThumbWheelFromDevice);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SettingsWindowMergeSavesAdministratorAndDeviceChoices(bool enabled)
    {
        var original = new AppSettings { RunAsAdministrator = !enabled,
            WindowKeys = new() { ThumbWheelFromDevice = !enabled } };
        var edited = original with { RunAsAdministrator = enabled,
            WindowKeys = original.WindowKeys with { ThumbWheelFromDevice = enabled } };
        var saved = SettingsText.MergeEditable(original, edited, editGap: false);
        Assert.Equal(enabled, saved.RunAsAdministrator);
        Assert.Equal(enabled, saved.WindowKeys.ThumbWheelFromDevice);
    }

    [Fact]
    public void TaskXmlPreservesUserPathsAndUnrestrictedBatteryRuntime()
    {
        const string exe = @"C:\日本語 & <test>\PartsTray.exe";
        const string folder = @"C:\日本語 & <test>";
        const string sid = "S-1-5-21-1000";
        var xml = XDocument.Parse(AutoStartTask.CreateXml(sid, exe, folder));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var root = xml.Root!;
        Assert.Equal(ns + "Task", root.Name);
        var trigger = root.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!;
        Assert.Equal("true", trigger.Element(ns + "Enabled")!.Value);
        Assert.Equal(sid, trigger.Element(ns + "UserId")!.Value);
        var principal = root.Element(ns + "Principals")!.Element(ns + "Principal")!;
        Assert.Equal(sid, principal.Element(ns + "UserId")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(ns + "RunLevel")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(ns + "LogonType")!.Value);
        var settings = root.Element(ns + "Settings")!;
        Assert.Equal("false", settings.Element(ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(ns + "StopIfGoingOnBatteries")!.Value);
        Assert.Equal("PT0S", settings.Element(ns + "ExecutionTimeLimit")!.Value);
        Assert.Equal("IgnoreNew", settings.Element(ns + "MultipleInstancesPolicy")!.Value);
        var exec = root.Element(ns + "Actions")!.Element(ns + "Exec")!;
        Assert.Equal(exe, exec.Element(ns + "Command")!.Value);
        Assert.Equal(folder, exec.Element(ns + "WorkingDirectory")!.Value);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ElevationPolicy(bool administrator, bool elevated, bool expected)
    {
        Assert.Equal(expected, WindowSwitchPolicy.IsCandidate(new(1, 10, IsElevated: elevated), 10, administrator));
        Assert.Equal(expected, WindowSwitchPolicy.IsCandidate(new(1, 10, true, false,
            WindowSwitchPolicy.RestoreToMaximized, IsElevated: elevated), 10, administrator));
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public void SelectionSkipsElevatedWindowOnlyAtNormalPrivilege(bool administrator, long expected)
    {
        WindowSwitchWindow[] windows = [new(1, 10), new(2, 10, IsElevated: true), new(3, 10)];
        var plan = WindowSwitchPolicy.Select(windows, 10, 1, false, administrator);
        Assert.Equal(expected, plan.Target);
        Assert.Equal(1, plan.SendToBottom);
    }
}
