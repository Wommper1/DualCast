using DualBluetooth.Audio;
using DualBluetooth.Models;

namespace DualBluetooth.Tests;

public sealed class AudioDeviceInfoTests
{
    private static AudioDeviceInfo Device(string name, bool active = true, bool isDefault = false, bool favorite = false) =>
        new(name, name, isDefault, active, favorite, active ? "active" : "disconnected");

    [Fact]
    public void DisplayName_ShowsFavoriteDefaultAndState()
    {
        Assert.Equal("Buds", Device("Buds").DisplayName);
        Assert.Equal("★  Buds (Windows default)", Device("Buds", isDefault: true, favorite: true).DisplayName);
        Assert.Equal("Buds — disconnected", Device("Buds", active: false).DisplayName);
    }

    [Fact]
    public void OrderDevices_FavoritesThenActiveThenDefaultThenName()
    {
        var ordered = DualAudioRouter.OrderDevices(
        [
            Device("Zed", active: false),
            Device("Beta"),
            Device("Alpha"),
            Device("Speakers", isDefault: true),
            Device("Fave", active: false, favorite: true)
        ]);

        Assert.Equal(["Fave", "Speakers", "Alpha", "Beta", "Zed"], ordered.Select(d => d.Name));
    }
}
