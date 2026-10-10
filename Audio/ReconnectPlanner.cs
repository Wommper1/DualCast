using DualBluetooth.Models;

namespace DualBluetooth.Audio;

public enum ReconnectState { Ready, Waiting, NotConfigured }

public sealed record ReconnectPlan(ReconnectState State, string Message);

/// <summary>
/// Decides whether a reconnect attempt can go ahead, from the saved choice of outputs and a fresh device list.
/// It goes by the saved device ids, not by what the on-screen lists show: once a device drops off, a refreshed list
/// marks it disconnected or leaves it out, and the on-screen selection can fall back to some other device.
/// </summary>
public static class ReconnectPlanner
{
    public static ReconnectPlan Plan(string? savedA, string? savedB, IReadOnlyList<AudioDeviceInfo> devices)
    {
        if (string.IsNullOrWhiteSpace(savedA) || string.IsNullOrWhiteSpace(savedB) ||
            string.Equals(savedA, savedB, StringComparison.Ordinal))
            return new ReconnectPlan(ReconnectState.NotConfigured, "Two different outputs must be chosen before streaming can resume.");

        var waiting = new List<string>();
        var a = devices.FirstOrDefault(device => device.Id == savedA);
        var b = devices.FirstOrDefault(device => device.Id == savedB);
        if (a is not { IsActive: true }) waiting.Add(a?.Name ?? "Output A");
        if (b is not { IsActive: true }) waiting.Add(b?.Name ?? "Output B");

        return waiting.Count == 0
            ? new ReconnectPlan(ReconnectState.Ready, string.Empty)
            : new ReconnectPlan(ReconnectState.Waiting, $"Waiting for {string.Join(" and ", waiting)} to reconnect…");
    }
}
