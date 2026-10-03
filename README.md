# DualCast

DualCast sends the Windows system mix to two Bluetooth playback devices at the same time. Each output has independent volume and synchronization delay controls.

## Run it

Open `dist/DualCast-OneFile/DualBluetooth.exe`, then:

1. Pair and connect both Bluetooth devices in Windows.
2. Choose two different devices under **Output A** and **Output B**. Use **★ Favorite** to pin a device to the top of the lists, and **Show disconnected** to keep a paired device visible while it is off. Your saved choice is remembered even while a device is disconnected.
3. Click **Start streaming**.

DualCast uses Windows process-loopback capture to collect system audio while excluding DualCast's own output. There is no separate capture-source device and no software feedback loop to configure.

## Calibrate synchronization

1. Stop streaming.
2. Click **Play sync test**. Four short pulses are sent to both outputs. Click **Stop test** to cancel early.
3. Add delay to whichever output you hear first, in 10 ms steps. Delay is applied when streaming starts, so the delay sliders and **Reset delays** are locked while streaming; stop, adjust, and start again.
4. Repeat until the pulses overlap.

Bluetooth delay can change after a reconnect. Small recalibrations are normal, especially when combining different device models. Devices, delays, volumes, buffer size, and reconnect preference save automatically to `%LOCALAPPDATA%\DualBluetooth\settings.json`.

## Stability controls

- **Stability buffer:** Start at 100 ms. Raise it to 150 or 250 ms if playback crackles or drops. Use 50 ms only when both devices are stable.
- **Reconnect automatically:** Restarts the audio pipeline after an active endpoint disconnects or stops playing unexpectedly. DualCast also listens for Windows playback-device changes and refreshes the device list when idle.
- **Session health:** Shows each output's queued audio, whether DualCast recently detected a Windows audio signal, and how many drift corrections have been applied.

For full-quality audio, use Stereo/A2DP playback and avoid enabling a Bluetooth headset microphone. Some Bluetooth hardware switches into a lower-quality communications profile when its microphone is active.

## Requirements and limitations

- Windows 10 version 2004 (build 19041) or later.
- .NET 10 Desktop Runtime when using the framework-dependent package in this repository.
- The Bluetooth adapter and driver must support two active playback connections. DualCast cannot add Bluetooth LE Audio or Auracast support to unsupported hardware.
- WASAPI loopback does not expose DRM-protected streams. Applications using exclusive-mode audio can also bypass the shared system mix.
- Different device clocks can drift during long sessions. DualCast keeps an independent queue per output with a startup cushion. If an output's queue grows more than about 120 ms past its starting level, DualCast skips a small packet of audio to pull it back, which can cause a faint click and is counted under Session health. A device that runs faster than the capture clock cannot be corrected this way and may still need a stream restart.

## Appearance and launcher

Use the gear button in the top-right corner to choose a dark color theme, accent color, text size, stronger high-visibility borders, and always-on-top behavior. DualCast defaults to Graphite with large text, keeps its Windows title bar dark, and follows Windows high-contrast mode.

The supported packaged launcher is `dist/DualCast-OneFile/DualBluetooth.exe`. Keep that exact filename: Windows application control may block renamed copies.

## Build from source

```powershell
dotnet restore DualBluetooth.slnx --configfile NuGet.Config
dotnet build DualBluetooth.slnx -c Release --no-restore
dotnet publish DualBluetooth.csproj -c Release --no-restore -o dist/DualCast
```

The app uses WPF on .NET 10 and NAudio 3's modern WASAPI recorder/player APIs. Capture and rendering use event-driven callbacks and MMCSS audio-thread priority. Shared-mode WASAPI performs output-format conversion for different devices.
