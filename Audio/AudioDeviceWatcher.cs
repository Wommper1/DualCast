using NAudio.CoreAudioApi;

namespace DualBluetooth.Audio;

public sealed class AudioDeviceWatcher : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;

    public AudioDeviceWatcher()
    {
        _notifications = _enumerator.CreateNotificationClient();
        _notifications.DeviceAdded += OnDevicesChanged;
        _notifications.DeviceRemoved += OnDevicesChanged;
        _notifications.DeviceStateChanged += OnDevicesChanged;
        _notifications.DefaultDeviceChanged += OnDevicesChanged;
    }

    public event EventHandler? DevicesChanged;

    private void OnDevicesChanged(object? sender, EventArgs e) => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _notifications.DeviceAdded -= OnDevicesChanged;
        _notifications.DeviceRemoved -= OnDevicesChanged;
        _notifications.DeviceStateChanged -= OnDevicesChanged;
        _notifications.DefaultDeviceChanged -= OnDevicesChanged;
        _notifications.Dispose();
        _enumerator.Dispose();
    }
}
