namespace DualBluetooth.Models;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault)
{
    public string DisplayName => IsDefault ? $"{Name} (Windows default)" : Name;

    public override string ToString() => DisplayName;
}
