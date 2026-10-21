# DualCast

DualCast sends the Windows system mix to two Bluetooth playback devices at the same time. Each output has independent volume and synchronization delay controls.

> **Early pre-release (1.0.x).** Tested on Windows 11. Streaming to two real Bluetooth devices is still being tested; see [VERIFICATION.md](VERIFICATION.md) for what has and has not been checked.

## Run it

Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) if you do not have it, unzip the release, and open `DualBluetooth.exe`. Windows SmartScreen may warn because the file is not code-signed; compare its zip with `SHA256SUMS.txt` on the release page first. Then:

1. Pair and connect both Bluetooth devices in Windows.
2. Choose two different devices under **Output A** and **Output B**. Use **★ Favorite** to pin a device to the top of the lists, and **Show disconnected** to keep a paired device visible while it is off. Your saved choice is remembered even while a device is disconnected.
3. Click **Start streaming**.

DualCast uses Windows process-loopback capture to collect system audio while excluding DualCast's own output. There is no separate capture-source device and no software feedback loop to configure.

## Avoid hearing everything twice

Windows keeps playing your audio on its **default output** as usual. DualCast captures that audio and plays a delayed copy to Output A and Output B. So if Output A or B is also the Windows default, you hear that device twice, as an echo. DualCast shows a warning when it sees this.

To avoid it, make a third device the Windows default (for example your PC speakers, turned down) in **Windows sound settings**, then choose your two Bluetooth devices in DualCast. The Windows volume controls act on the default device, so use DualCast's two volume sliders for the Bluetooth devices.

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

## Troubleshooting

| What you see | What to try |
| --- | --- |
| "Could not start" | Both devices must show as connected. Open **Bluetooth devices**, connect them, then click **Refresh devices**. Close other apps that use a device exclusively. |
| Crackling or dropouts | Raise the **Stability buffer** to 150 or 250 ms, move closer to the PC, and keep Bluetooth headset microphones off so the devices stay in Stereo/A2DP mode. |
| One device is late or louder | Run the sync test, add delay to the device you hear first, and use the two volume sliders. |
| A faint click every few seconds | Two Bluetooth devices run on slightly different clocks, so DualCast occasionally skips a tiny packet. **Session health** counts these as drift corrections. This is a known limit of the current version. |
| No sound on either device | Check that Session health says "signal active" while something is playing. Copy-protected (DRM) audio and apps using exclusive-mode audio are not captured. |
| Windows SmartScreen warns | The program is not code-signed yet. Compare the zip with `SHA256SUMS.txt` on the release page before running it. |

## Reporting a problem

Open an issue at https://github.com/Wommper1/DualCast/issues and fill in the form. It asks for the DualCast version (right-click `DualBluetooth.exe`, Properties, Details), your Windows version, your Bluetooth adapter and the two devices. The log is at `%LOCALAPPDATA%\DualBluetooth\logs\dualcast.log`; read it before sharing it, because error text can include device names. For security problems, use the private route in [SECURITY.md](SECURITY.md) instead of a public issue.

## Requirements and limitations

- Windows 10 version 2004 (build 19041) or later.
- .NET 10 Desktop Runtime (x64). The release package does not bundle it.
- The Bluetooth adapter and driver must support two active playback connections. DualCast cannot add Bluetooth LE Audio or Auracast support to unsupported hardware.
- WASAPI loopback does not expose DRM-protected streams. Applications using exclusive-mode audio can also bypass the shared system mix.
- Different device clocks can drift during long sessions. DualCast keeps an independent queue per output with a startup cushion. If an output's queue grows more than about 120 ms past its starting level, DualCast skips a small packet of audio to pull it back, which can cause a faint click and is counted under Session health. A device that runs faster than the capture clock cannot be corrected this way and may still need a stream restart.

## Appearance and launcher

Use the gear button in the top-right corner to choose a dark color theme, accent color, text size, stronger high-visibility borders, and always-on-top behavior. DualCast defaults to Graphite with large text, keeps its Windows title bar dark, and follows Windows high-contrast mode.

The packaged launcher is `DualBluetooth.exe` from the release zip. Keep that exact filename: Windows application control may block renamed copies.

## Build from source

```powershell
dotnet restore DualBluetooth.slnx --configfile NuGet.Config
dotnet build DualBluetooth.slnx -c Release --no-restore
dotnet publish DualBluetooth.csproj -c Release --no-restore -o dist/DualCast
# Or build the release zip, checksum and personal-data check in one go:
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

Run the tests with `dotnet test tests/DualBluetooth.Tests`. They cover settings migration and saving, calibration-signal timing, input validation, drift correction, and device ordering. Streaming itself needs real Bluetooth hardware and is not covered.

The app uses WPF on .NET 10 and NAudio 3's modern WASAPI recorder/player APIs. Capture and rendering use event-driven callbacks and MMCSS audio-thread priority. Shared-mode WASAPI performs output-format conversion for different devices.

## More

[Privacy](PRIVACY.md) | [Security](SECURITY.md) | [What was tested](VERIFICATION.md) | [Changelog](CHANGELOG.md) | [Third-party notices](THIRD_PARTY_NOTICES.md) | [MIT license](LICENSE)
