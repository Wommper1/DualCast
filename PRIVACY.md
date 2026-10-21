# Privacy

DualCast is local-only. It has no account, no cloud service, no telemetry and no update check.

## Network

The app contains no networking code. It never contacts a server, and nothing about you, your devices or your audio leaves the PC. The only things it opens outside its own window are the Windows Settings pages for Sound and Bluetooth, when you click the matching buttons. (Checked in the source on 2026-10-21 by searching for network APIs; this file must be updated if that ever changes.)

## Audio

DualCast listens to the Windows system mix through Windows process-loopback capture, excluding its own output, and plays it to the two outputs you chose. Audio passes through in-memory buffers only. It is never written to disk, recorded or transmitted. The sync test plays short generated pulses, not your audio.

Because it hears the whole system mix, it hears whatever other programs play, the same as any loopback tool. It does not use a microphone.

## What is stored on your PC

Everything is under `%LOCALAPPDATA%\DualBluetooth`:

| File | Holds |
| --- | --- |
| `settings.json` | Windows device IDs of the two chosen outputs and your favorites, delays, volumes, buffer size, reconnect and display choices |
| `logs\dualcast.log` (and `.old`) | Start and stop times, the Windows version, delay and buffer values, drift and gap counts, and error text from Windows. Error text can include device names. The log rotates at 512 KB, so it stays under about 1 MB |

Device IDs and log lines stay on the PC. If you send a log to someone for support, read it first.

## Removing it

Close DualCast and delete the `%LOCALAPPDATA%\DualBluetooth` folder and the program folder. Nothing is installed system-wide and nothing is added to the registry by the app.
