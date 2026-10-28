# Case study: one audio mix, two independent outputs

## At a glance
**Alex Shade · independent, AI-assisted experiment.** [v1.0.1 pre-release](https://github.com/Wommper1/DualCast/releases/tag/v1.0.1), Windows. Evidence reviewed October 28, 2026; no new hardware tests performed for this document.

## The challenge
Explore playing the same Windows audio through two Bluetooth devices, with a way to adjust their timing and volume separately.

## The investigation
[VERIFICATION.md](VERIFICATION.md) makes the key distinction: settings and routing logic can be checked automatically, but real simultaneous playback, drift and reconnection need hardware. [The README](README.md) explains adapter requirements, default-output echo and differing device clocks.

## The approach and build
The repository uses C#, WPF, .NET 10 and NAudio. Windows process-loopback captures the system mix while excluding DualCast's own output. Independent queues feed two selected outputs, with per-device volume and delay, a sync test and health information. See [DualAudioRouter.cs](Audio/DualAudioRouter.cs), [PrimedBuffer.cs](Audio/PrimedBuffer.cs) and [RoutingOptions.cs](Audio/RoutingOptions.cs).

Alex's part is the project direction and AI-assisted development and troubleshooting. The repository demonstrates a software implementation; it does not establish production audio-engineering expertise or full hardware qualification. There is no authentic app screenshot in this showcase yet.

## The obstacles
Different output clocks cause drift. The verification record notes packet-skipping correction and possible clicks; smooth resampling is not part of 1.0.x. Using an output as the Windows default can create echo. These documented limits inform the first-run and calibration instructions.

## The result and limits
The verification record reports 46 automated tests passed and a downloaded v1.0.1 launch test on Windows 11. Those are prior documented results, not checks rerun here. Streaming to two real devices for 30 minutes, two-hour drift, reconnection, Windows 10 and a runtime-only PC remain unchecked there. The Bluetooth adapter/driver must support two playback connections.

## Try it / what comes next
Start with [Run it](README.md#run-it), meet the .NET runtime requirements, and read [Requirements and limitations](README.md#requirements-and-limitations). The highest-value contribution is a reproducible hardware report addressing [the open verification checklist](VERIFICATION.md). Do not treat an audible sync estimate as a measured latency guarantee.

Use the [bug-report form](https://github.com/Wommper1/DualCast/issues/new/choose); redact device names if private. Follow [SECURITY.md](SECURITY.md) for vulnerability reports. Small code proposals should explain the behavior changed, tests run, and any hardware checks still needed; discuss broad routing changes in an issue first.

[Meet Alex](https://github.com/Wommper1) · [The Level Everything Company](https://leveleverythingcompany.github.io/)
