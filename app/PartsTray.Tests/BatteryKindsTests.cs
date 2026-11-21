// Bluetooth の種類判定と、12 個の電池枠への割り当てを検証します。
// 実機のプロパティ値・優先順位・切断時の規則を追加する場所です。
using PartsTray.Core;
using Xunit;

namespace PartsTray.Tests;

public sealed class BatteryKindsTests
{
    [Theory]
    [InlineData(2360324, BatteryDeviceKind.Headphones)]
    [InlineData(2360344, BatteryDeviceKind.Headphones)]
    [InlineData(0, null)]
    [InlineData(0x0540, BatteryDeviceKind.Keyboard)]
    [InlineData(0x0580, BatteryDeviceKind.Mouse)]
    [InlineData(0x05C0, BatteryDeviceKind.Keyboard)]
    [InlineData(0x0504, BatteryDeviceKind.Gamepad)]
    [InlineData(0x0508, BatteryDeviceKind.Gamepad)]
    [InlineData(0x050C, null)]
    [InlineData(0x0500, null)]
    [InlineData(0x0204, null)]
    [InlineData(0x240580, BatteryDeviceKind.Mouse)]
    public void ClassOfDeviceUsesMajorAndMinorBits(int value, BatteryDeviceKind? expected) =>
        Assert.Equal(expected, BatteryKinds.FromClassOfDevice((uint)value));

    [Theory]
    [InlineData("HID_DEVICE_SYSTEM_MOUSE", BatteryDeviceKind.Mouse)]
    [InlineData("hid_device_system_keyboard", BatteryDeviceKind.Keyboard)]
    [InlineData("HID_DEVICE_SYSTEM_GAME", BatteryDeviceKind.Gamepad)]
    [InlineData("HID_DEVICE_SYSTEM_MOUSE_EXTRA", null)]
    [InlineData("HID_DEVICE", null)]
    public void CompatibleIdsAreCaseInsensitiveExactMatches(string id, BatteryDeviceKind? expected) =>
        Assert.Equal(expected, BatteryKinds.FromHid(["HID_DEVICE", id]));

    [Theory]
    [InlineData("AirPods 4", BatteryDeviceKind.Earbuds)]
    [InlineData("Galaxy BUDS", BatteryDeviceKind.Earbuds)]
    [InlineData("WF-1000XM5", BatteryDeviceKind.Earbuds)]
    [InlineData("wireless earbuds", BatteryDeviceKind.Earbuds)]
    [InlineData("新しいイヤホン", BatteryDeviceKind.Earbuds)]
    [InlineData("Pro Controller", BatteryDeviceKind.Gamepad)]
    [InlineData("Xbox controller", BatteryDeviceKind.Gamepad)]
    [InlineData("GAMEPAD", BatteryDeviceKind.Gamepad)]
    [InlineData("DualSense", BatteryDeviceKind.Gamepad)]
    [InlineData("DUALSHOCK 4", BatteryDeviceKind.Gamepad)]
    [InlineData("Bluetooth Mouse", BatteryDeviceKind.Mouse)]
    [InlineData("静音マウス", BatteryDeviceKind.Mouse)]
    [InlineData("keyboard", BatteryDeviceKind.Keyboard)]
    [InlineData("キーボード", BatteryDeviceKind.Keyboard)]
    [InlineData("iPhone", BatteryDeviceKind.Other)]
    [InlineData("WH-1000XM6", BatteryDeviceKind.Other)]
    public void NameFallback(string name, BatteryDeviceKind expected) => Assert.Equal(expected, BatteryKinds.Detect(name));

    [Fact]
    public void PriorityAndAudioRefinement()
    {
        Assert.Equal(BatteryDeviceKind.Other, BatteryKinds.Detect("AirPods Mouse", "other", ["HID_DEVICE_SYSTEM_MOUSE"], 0x0540));
        Assert.Equal(BatteryDeviceKind.Mouse, BatteryKinds.Detect("AirPods", compatibleIds: ["HID_DEVICE_SYSTEM_MOUSE"], classOfDevice: 0x0540));
        Assert.Equal(BatteryDeviceKind.Keyboard, BatteryKinds.Detect("AirPods Mouse", classOfDevice: 0x0540));
        Assert.Equal(BatteryDeviceKind.Headphones, BatteryKinds.Detect("Controller", classOfDevice: 2360324));
        Assert.Equal(BatteryDeviceKind.Headphones, BatteryKinds.Detect("WH-1000XM6", classOfDevice: 2360324));
        Assert.Equal(BatteryDeviceKind.Earbuds, BatteryKinds.Detect("AirPods 4", classOfDevice: 2360344));
        Assert.Equal(BatteryDeviceKind.Other, BatteryKinds.Detect("iPhone", classOfDevice: 0));
        Assert.Equal(BatteryDeviceKind.Mouse, BatteryKinds.FromHid(["HID_DEVICE_SYSTEM_GAME", "HID_DEVICE_SYSTEM_KEYBOARD", "HID_DEVICE_SYSTEM_MOUSE"]));
    }

    private static BatteryDevice Device(string name, BatteryDeviceKind kind, bool connected = true) =>
        new("bluetooth:" + name, name, kind, connected, new(50, BatteryLevel.Unknown, BatteryCharge.Unknown, false, false), null);

    [Fact]
    public void PrimarySlotsAndOverflowAreStableAcrossArrivalOrders()
    {
        var devices = new[] { Device("A mouse", BatteryDeviceKind.Mouse), Device("B keyboard", BatteryDeviceKind.Keyboard),
            Device("C headphones", BatteryDeviceKind.Headphones), Device("D earbuds", BatteryDeviceKind.Earbuds),
            Device("E gamepad", BatteryDeviceKind.Gamepad), Device("F other", BatteryDeviceKind.Other),
            Device("Z mouse", BatteryDeviceKind.Mouse), Device("H keyboard", BatteryDeviceKind.Keyboard), Device("G other", BatteryDeviceKind.Other) };
        var slots = BatteryLayout.Assign(devices.Reverse(), new());
        // 11/20：スマホ（6）・タブレット（7）の固定枠を足して 14 枠。同じ種類の 2 台目以降は 8 から。
        Assert.Equal(14, slots.Length);
        Assert.Equal(devices.Take(6), slots.Take(6));
        Assert.Null(slots[6]); Assert.Null(slots[7]);
        Assert.Equal(new[] { "G other", "H keyboard", "Z mouse" }, slots.Skip(8).Take(3).Select(d => d!.Name));
        Assert.Equal(slots, BatteryLayout.Assign(devices, new()));
    }

    [Theory]
    [InlineData(BatteryDeviceKind.Mouse, true)] [InlineData(BatteryDeviceKind.Keyboard, true)]
    [InlineData(BatteryDeviceKind.Headphones, false)] [InlineData(BatteryDeviceKind.Earbuds, false)]
    [InlineData(BatteryDeviceKind.Gamepad, false)] [InlineData(BatteryDeviceKind.Other, true)]
    public void DisconnectPolicy(BatteryDeviceKind kind, bool shown) =>
        Assert.Equal(shown, BatteryLayout.Assign([Device("test", kind, false)], new()).Any(d => d != null));

    [Fact]
    public void OverridesAndFiltersApplyBeforeAssignment()
    {
        var d = Device("test", BatteryDeviceKind.Headphones, false);
        var s = new BatterySettings { Kinds = new() { ["test"] = "mouse" } };
        Assert.Equal(BatteryDeviceKind.Mouse, BatteryLayout.Assign([d], s)[0]!.Kind);
        Assert.All(BatteryLayout.Assign([d], s with { Hidden = ["test"] }), Assert.Null);
        Assert.All(BatteryLayout.Assign([d], s with { Enabled = false }), Assert.Null);
        Assert.All(BatteryLayout.Assign([d], s with { Bluetooth = false }), Assert.Null);
        Assert.All(BatteryLayout.Assign([d with { Reading = null }], s), Assert.Null);
        Assert.Null(BatteryLayout.Assign([d with { Name = "Test" }], s)[0]);
        Assert.All(BatteryLayout.Assign([d with { Id = "logitech:1" }], s with { Logitech = false }), Assert.Null);
    }

    [Fact]
    public void OverflowIsBoundedAndDoesNotOccupyMissingPrimarySlots()
    {
        var slots = BatteryLayout.Assign(Enumerable.Range(0, 20).Select(i => Device($"Mouse {i:00}", BatteryDeviceKind.Mouse)), new());
        Assert.Equal(7, slots.Count(d => d != null));
        Assert.All(slots.Skip(1).Take(7), Assert.Null);
        Assert.Equal("Mouse 06", slots[13]!.Name);
    }

    // 11/20：スマホとタブレットはそれぞれ固定の枠（6・7）。切断中は隠す（ヘッドホンなどと同じ）。
    [Fact]
    public void PhoneAndTabletHaveTheirOwnSlotsAndHideWhenDisconnected()
    {
        var phone = Device("iPhone", BatteryDeviceKind.Phone) with { Id = "apple:p" };
        var tablet = Device("iPad", BatteryDeviceKind.Tablet) with { Id = "apple:t" };
        var slots = BatteryLayout.Assign([phone, tablet], new());
        Assert.Same(phone, slots[6]); Assert.Same(tablet, slots[7]);
        Assert.All(BatteryLayout.Assign([phone with { Connected = false }, tablet with { Connected = false }], new()), Assert.Null);
        Assert.True(BatteryKinds.TryParse("phone", out var k1)); Assert.Equal(BatteryDeviceKind.Phone, k1);
        Assert.True(BatteryKinds.TryParse("tablet", out var k2)); Assert.Equal(BatteryDeviceKind.Tablet, k2);
        Assert.Equal("スマホ", BatteryKinds.Label(BatteryDeviceKind.Phone)); Assert.Equal("タブレット", BatteryKinds.Label(BatteryDeviceKind.Tablet));
    }
}
