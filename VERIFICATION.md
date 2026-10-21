# Verification

What was checked, what was not. Update this before every release.

## Checked (2026-10-21)

| Check | Result |
| --- | --- |
| Unit tests (`dotnet test tests/DualBluetooth.Tests -c Release`, SDK 10.0.401) | 46 of 46 passed |
| Secrets sweep of the working tree, full git history and stashes | Nothing found |
| `secret-guard` over the whole history | Clean |
| Commit hook | Blocks a staged fake token and a `.pem` file in a throwaway repo; allows a clean commit |
| Known-vulnerable or deprecated NuGet packages | None (NAudio 3.3.0) |
| Personal data in the built `.exe` and `.pdb` | None after the `PathMap` setting; the older builds in `dist\` contained the Windows user name and folder path and must not be shipped |
| Network code in the source | None |

## Not checked: needs you and the hardware

| Check | Why it matters |
| --- | --- |
| [ ] Stream to two real Bluetooth devices for 30 minutes | The tests do not cover streaming |
| [ ] Sync test and delay calibration by ear | Needs two devices |
| [ ] Switch one device off and on during streaming (auto-reconnect) | Only the planning logic is unit-tested |
| [ ] Drift over a long session (2 hours) | Device clocks differ |
| [ ] Run the release zip on a PC that has only the .NET 10 Desktop Runtime | Confirms no hidden dependency on this PC |
| [ ] Windows 10 (build 19041 or later) | Built and tested on Windows 11 only |
| [ ] High-contrast mode and a screen reader | Only checked visually |
| [ ] Run all the streaming checks above on 1.0.1 | NAudio moved from 3.0.1 to 3.3.0 and streaming has not been tested on either version |

## Known limits

- The executable is unsigned, so SmartScreen may warn.
- The Bluetooth adapter and driver must support two active playback connections; DualCast cannot add that.
