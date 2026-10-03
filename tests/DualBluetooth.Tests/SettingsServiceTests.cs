using System.IO;
using DualBluetooth.Models;
using DualBluetooth.Services;

namespace DualBluetooth.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DualCastTests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = new SettingsService(SettingsPath).Load();

        Assert.Equal(100, settings.RenderLatencyMilliseconds);
        Assert.Equal("Graphite", settings.Theme);
        Assert.Empty(settings.FavoriteDeviceIds);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var service = new SettingsService(SettingsPath);
        service.Save(new AppSettings
        {
            OutputADeviceId = "a",
            OutputBDeviceId = "b",
            DelayBMilliseconds = 120,
            VolumeAPercent = 55,
            Theme = "Oled",
            FavoriteDeviceIds = ["a"]
        });

        var loaded = service.Load();

        Assert.Equal("a", loaded.OutputADeviceId);
        Assert.Equal("b", loaded.OutputBDeviceId);
        Assert.Equal(120, loaded.DelayBMilliseconds);
        Assert.Equal(55, loaded.VolumeAPercent);
        Assert.Equal("Oled", loaded.Theme);
        Assert.Equal(["a"], loaded.FavoriteDeviceIds);
    }

    [Fact]
    public void Save_LeavesNoTempFileAndOverwritesExisting()
    {
        var service = new SettingsService(SettingsPath);
        service.Save(new AppSettings { DelayAMilliseconds = 10 });
        service.Save(new AppSettings { DelayAMilliseconds = 20 });

        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.Equal(20, service.Load().DelayAMilliseconds);
    }

    [Theory]
    [InlineData("""{"Theme":"Oled","TextSize":14,"HighVisibility":false,"ShowUnavailableDevices":false}""")]
    [InlineData("""{"SettingsVersion":1,"Theme":"Oled","TextSize":14,"HighVisibility":false,"ShowUnavailableDevices":false}""")]
    public void Load_OldVersion_AppliesMigrations(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);

        var settings = new SettingsService(SettingsPath).Load();

        Assert.Equal(3, settings.SettingsVersion);
        Assert.Equal("Graphite", settings.Theme);
        Assert.Equal(16, settings.TextSize);
        Assert.True(settings.HighVisibility);
        Assert.True(settings.ShowUnavailableDevices);
    }

    [Fact]
    public void Load_CurrentVersion_KeepsUserChoices()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"SettingsVersion":3,"Theme":"Oled","TextSize":14,"HighVisibility":false,"ShowUnavailableDevices":false}""");

        var settings = new SettingsService(SettingsPath).Load();

        Assert.Equal("Oled", settings.Theme);
        Assert.Equal(14, settings.TextSize);
        Assert.False(settings.HighVisibility);
        Assert.False(settings.ShowUnavailableDevices);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = new SettingsService(SettingsPath).Load();

        Assert.Equal(100, settings.RenderLatencyMilliseconds);
    }

    [Fact]
    public void Load_NullFavoriteList_BecomesEmpty()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"SettingsVersion":3,"FavoriteDeviceIds":null}""");

        Assert.NotNull(new SettingsService(SettingsPath).Load().FavoriteDeviceIds);
    }
}
