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
            var isOlderSettings = !document.RootElement.TryGetProperty(nameof(AppSettings.SettingsVersion), out var version)
                || version.GetInt32() < 2;
            if (isOlderSettings)
            {
                settings.SettingsVersion = 2;
                settings.Theme = "Graphite";
                settings.TextSize = 16;
                settings.HighVisibility = true;
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
