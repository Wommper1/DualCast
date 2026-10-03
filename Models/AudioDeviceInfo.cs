namespace DualBluetooth.Models;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault,
    bool IsActive,
    bool IsFavorite,
    string StateLabel)
{
    public string DisplayName
    {
        get
        {
            var favorite = IsFavorite ? "★  " : string.Empty;
            var defaultLabel = IsDefault ? " (Windows default)" : string.Empty;
            var state = IsActive ? string.Empty : $" — {StateLabel}";
            return $"{favorite}{Name}{defaultLabel}{state}";
        }
    }

    public override string ToString() => DisplayName;
}
