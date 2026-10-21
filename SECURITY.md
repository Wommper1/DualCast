# Security policy

## Reporting a vulnerability

Please report security problems **privately**, not in a public issue.

Use GitHub's private reporting: open the repository's **Security** tab and choose **Report a vulnerability**. Include what you found, the version (in the executable's Properties, Details), and steps to reproduce. This is a small project, so please allow a few days for an answer.

Only the latest release is supported with fixes.

## What the app is, for threat-modelling

DualCast is a local Windows desktop app. It captures the system audio mix and plays it to two Bluetooth outputs. It has no network code, no accounts and no telemetry. See [PRIVACY.md](PRIVACY.md).

| Area | What it does | Safeguards |
| --- | --- | --- |
| Audio capture | Windows process-loopback capture of the system mix, excluding DualCast itself | Audio stays in memory and is never written or sent anywhere |
| Files | `settings.json` and a size-capped log under `%LOCALAPPDATA%\DualBluetooth` | A damaged settings file falls back to defaults, and the app never runs anything it reads from the files |
| Started programs | Only the Windows Settings pages for Sound and Bluetooth (`ms-settings:` links) | Fixed links, no user input |
| Privileges | Runs as the current user | Needs no administrator rights and installs no service or driver |

## Known limitations

Please do not report these as vulnerabilities, but do say if you see a way to improve them:

- **The executable is not code-signed**, so Windows SmartScreen may warn the first time. Check your download against `SHA256SUMS.txt` on the release page.
- **It hears the whole system mix**, including other apps, by design.
- **A person who can already run code as you, or edit the app folder, can change what the app does.**
- **Bluetooth security is Windows' job.** DualCast adds no pairing, encryption or authentication of its own.

## Verifying a download

```powershell
Get-FileHash .\DualCast-1.0.0-win-x64.zip -Algorithm SHA256
```

The result must equal the line for that file in `SHA256SUMS.txt` on the same release page.

## Dependencies

NAudio (MIT) is the only direct dependency. CI fails on any known-vulnerable NuGet package, and Dependabot opens update pull requests weekly. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
