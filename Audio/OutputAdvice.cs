using DualBluetooth.Models;

namespace DualBluetooth.Audio;

public static class OutputAdvice
{
    /// <summary>
    /// Windows keeps playing the normal system mix on its default output. DualCast captures that mix and plays a delayed
    /// copy to each chosen output, so a chosen output that is also the Windows default is heard twice, as an echo.
    /// </summary>
    public static string? DefaultDeviceEcho(IReadOnlyList<AudioDeviceInfo> devices, string? idA, string? idB)
    {
        var doubled = devices.FirstOrDefault(device =>
            device.IsDefault && (device.Id == idA || device.Id == idB) && !string.IsNullOrEmpty(device.Id));
        return doubled is null
            ? null
            : $"{doubled.Name} is the Windows default output, so Windows already plays your audio there and DualCast adds a " +
              "delayed copy: you will hear an echo. Make a different device the Windows default (for example your speakers, " +
              "turned down) before streaming.";
    }
}
