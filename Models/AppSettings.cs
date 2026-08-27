namespace DualBluetooth.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 2;
    public string? OutputADeviceId { get; set; }
    public string? OutputBDeviceId { get; set; }
    public int DelayAMilliseconds { get; set; }
    public int DelayBMilliseconds { get; set; }
    public int VolumeAPercent { get; set; } = 100;
    public int VolumeBPercent { get; set; } = 100;
    public int RenderLatencyMilliseconds { get; set; } = 100;
    public bool AutoReconnect { get; set; } = true;
    public string Theme { get; set; } = "Graphite";
    public string Accent { get; set; } = "Ocean";
    public int TextSize { get; set; } = 16;
    public bool HighVisibility { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
}
