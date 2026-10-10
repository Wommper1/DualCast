using DualBluetooth.Audio;
using DualBluetooth.Models;

namespace DualBluetooth.Tests;

public sealed class OutputAdviceTests
{
    private static AudioDeviceInfo Device(string id, string name, bool isDefault = false) =>
        new(id, name, isDefault, true, false, "Active");

    [Fact]
    public void WarnsWhenAChosenOutputIsTheWindowsDefault()
    {
        var devices = new[] { Device("a", "Left buds", isDefault: true), Device("b", "Right buds"), Device("c", "Speakers") };
        var advice = OutputAdvice.DefaultDeviceEcho(devices, "a", "b");
        Assert.NotNull(advice);
        Assert.Contains("Left buds", advice);
        Assert.Contains("echo", advice);
    }

    [Fact]
    public void WarnsAboutTheSecondOutputToo()
    {
        var devices = new[] { Device("a", "Left buds"), Device("b", "Right buds", isDefault: true) };
        Assert.Contains("Right buds", OutputAdvice.DefaultDeviceEcho(devices, "a", "b"));
    }

    [Fact]
    public void StaysQuietWhenTheDefaultIsAnotherDevice()
    {
        var devices = new[] { Device("a", "Left buds"), Device("b", "Right buds"), Device("c", "Speakers", isDefault: true) };
        Assert.Null(OutputAdvice.DefaultDeviceEcho(devices, "a", "b"));
    }

    [Fact]
    public void StaysQuietWhenNothingIsChosenOrNothingIsDefault()
    {
        var devices = new[] { Device("a", "Left buds"), Device("b", "Right buds") };
        Assert.Null(OutputAdvice.DefaultDeviceEcho(devices, "a", "b"));
        Assert.Null(OutputAdvice.DefaultDeviceEcho([Device("a", "Left buds", isDefault: true)], null, null));
        Assert.Null(OutputAdvice.DefaultDeviceEcho([], "a", "b"));
    }
}
