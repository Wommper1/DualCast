namespace DualBluetooth.Audio;

public sealed record RoutingOptions(
    string OutputADeviceId,
    string OutputBDeviceId,
    int DelayAMilliseconds,
    int DelayBMilliseconds,
    float VolumeA,
    float VolumeB,
    int RenderLatencyMilliseconds);
