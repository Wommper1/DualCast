# Changelog

## 1.0.1 (pre-release)

- Updates the audio library NAudio from 3.0.1 to 3.3.0. Nothing else in the app changed. It builds with no warnings and all 46 tests pass, but streaming has not been tested on real Bluetooth hardware with either version yet.
- NAudio's Asio, Dmo and WinForms parts are no longer pulled in, so there are fewer dependencies.

## 1.0.0 (pre-release, 2026-10-21)

First public version. Release as a **pre-release** until the two-device hardware checks in [VERIFICATION.md](VERIFICATION.md) are done.

- Sends the Windows system mix to two Bluetooth playback devices at once, with independent volume and delay for each.
- Windows process-loopback capture, so there is no capture-source device to pick and no feedback loop.
- Sync test and 10 ms delay steps for calibration.
- Stability buffer (50, 100, 150, 250 ms) built on a primed jitter buffer, drift correction, and a session-health panel.
- Automatic reconnect by saved outputs, device favorites, and a list that refreshes when Windows playback devices change.
- Dark themes, accent colors, large text, high-visibility borders and Windows high-contrast support.
- Single-instance guard, safe close while starting, and a size-capped log under `%LOCALAPPDATA%\DualBluetooth`.
- Release hygiene: MIT license, privacy and security notes, build paths removed from the executable, secret-blocking rules, CI and a release script.
