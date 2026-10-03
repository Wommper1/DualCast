using System.IO;
using System.Text.Json;
using DualBluetooth.Models;

namespace DualBluetooth.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(appData, "DualBluetooth", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            using var document = JsonDocument.Parse(json);
            var loadedVersion = document.RootElement.TryGetProperty(nameof(AppSettings.SettingsVersion), out var version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out var parsedVersion)
                    ? parsedVersion
                    : 0;
            if (loadedVersion < 2)
            {
                settings.SettingsVersion = 2;
                settings.Theme = "Graphite";
                settings.TextSize = 16;
                settings.HighVisibility = true;
            }
            if (loadedVersion < 3)
            {
                settings.SettingsVersion = 3;
                settings.ShowUnavailableDevices = true;
            }
            settings.FavoriteDeviceIds ??= [];
            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings load failed; using defaults", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        // Write beside the target then swap, so a crash mid-write can't leave a truncated settings file.
        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);
    }
}
