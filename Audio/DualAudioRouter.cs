using System.IO;
using System.Runtime.InteropServices;
using DualBluetooth.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DualBluetooth.Audio;

public sealed class DualAudioRouter : IDisposable
{
    private readonly object _bufferGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private WasapiRecorder? _capture;
    private WasapiPlayer? _outputA;
    private WasapiPlayer? _outputB;
    private BufferedWaveProvider? _bufferA;
    private BufferedWaveProvider? _bufferB;
    private VolumeSampleProvider? _volumeA;
    private VolumeSampleProvider? _volumeB;
    private MMDevice? _outputDeviceA;
    private MMDevice? _outputDeviceB;
    private bool _stopping;
    private long _lastAudioSignalTicks;
    private double _targetBufferAMilliseconds;
    private double _targetBufferBMilliseconds;
    private long _driftDropCount;

    // How far past its starting level a queue may grow before incoming audio is dropped.
    internal const double MaxDriftMilliseconds = 120;

    public event EventHandler<string>? Faulted;

    public bool IsRunning { get; private set; }

    public bool HasRecentAudioSignal
    {
        get
        {
            var ticks = Volatile.Read(ref _lastAudioSignalTicks);
            return ticks != 0 && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < TimeSpan.FromSeconds(2);
        }
    }

    /// <summary>Capture packets skipped because an output's clock fell behind the capture clock.</summary>
    public long DriftDropCount => Interlocked.Read(ref _driftDropCount);

    public (double A, double B) BufferedMilliseconds
    {
        get
        {
            lock (_bufferGate)
                return (_bufferA?.BufferedDuration.TotalMilliseconds ?? 0,
                        _bufferB?.BufferedDuration.TotalMilliseconds ?? 0);
        }
    }

    public static IReadOnlyList<AudioDeviceInfo> GetRenderDevices(IEnumerable<string>? favoriteDeviceIds, bool includeUnavailable)
    {
        using var enumerator = new MMDeviceEnumerator();
        var favorites = new HashSet<string>(favoriteDeviceIds ?? [], StringComparer.Ordinal);
        string? defaultId = null;
        try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
        catch { }

        var endpoints = enumerator.EnumerateAudioEndPoints(
            DataFlow.Render,
            includeUnavailable ? DeviceState.All : DeviceState.Active);
        var devices = new List<AudioDeviceInfo>();
        foreach (var endpoint in endpoints)
        {
            try
            {
                var state = endpoint.State;
                devices.Add(new AudioDeviceInfo(
                    endpoint.ID,
                    endpoint.FriendlyName,
                    endpoint.ID == defaultId,
                    state == DeviceState.Active,
                    favorites.Contains(endpoint.ID),
                    DeviceStateLabel(state)));
            }
            catch
            {
                // A stale driver endpoint can disappear while Windows is enumerating it.
            }
            finally
            {
                endpoint.Dispose();
            }
        }

        return OrderDevices(devices);
    }

    internal static IReadOnlyList<AudioDeviceInfo> OrderDevices(IEnumerable<AudioDeviceInfo> devices) =>
        devices
            .OrderByDescending(device => device.IsFavorite)
            .ThenByDescending(device => device.IsActive)
            .ThenByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string DeviceStateLabel(DeviceState state) => state switch
    {
        DeviceState.Disabled => "disabled",
        DeviceState.Unplugged => "disconnected",
        DeviceState.NotPresent => "not connected",
        _ => "unavailable"
    };

    public async Task StartAsync(RoutingOptions options)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (IsRunning)
                return;

            ValidateOutputs(options);
            _stopping = false;

            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                    throw new PlatformNotSupportedException("Feedback-free system capture requires Windows 10 version 2004 or newer.");

                var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
                _capture = await new WasapiRecorderBuilder()
                    .WithProcessLoopback((uint)Environment.ProcessId, ProcessLoopbackMode.ExcludeTargetProcessTree)
                    .WithFormat(format)
                    .WithBufferLength(100)
                    .WithEventSync()
                    .WithMmcssThreadPriority("Audio")
                    .BuildAsync();

                using var enumerator = new MMDeviceEnumerator();
                _outputDeviceA = enumerator.GetDevice(options.OutputADeviceId);
                _outputDeviceB = enumerator.GetDevice(options.OutputBDeviceId);
                _targetBufferAMilliseconds = options.DelayAMilliseconds + options.RenderLatencyMilliseconds;
                _targetBufferBMilliseconds = options.DelayBMilliseconds + options.RenderLatencyMilliseconds;
                Interlocked.Exchange(ref _driftDropCount, 0);
                _bufferA = CreateBuffer(format, _targetBufferAMilliseconds);
                _bufferB = CreateBuffer(format, _targetBufferBMilliseconds);
                _volumeA = new VolumeSampleProvider(_bufferA.ToSampleProvider()) { Volume = ClampVolume(options.VolumeA) };
                _volumeB = new VolumeSampleProvider(_bufferB.ToSampleProvider()) { Volume = ClampVolume(options.VolumeB) };

                _outputA = CreatePlayer(_outputDeviceA, options.RenderLatencyMilliseconds);
                _outputB = CreatePlayer(_outputDeviceB, options.RenderLatencyMilliseconds);
                _outputA.Init(_volumeA.ToWaveProvider());
                _outputB.Init(_volumeB.ToWaveProvider());

                _capture.DataAvailable += Capture_DataAvailable;
                _capture.RecordingStopped += Capture_RecordingStopped;
                _outputA.PlaybackStopped += Output_PlaybackStopped;
                _outputB.PlaybackStopped += Output_PlaybackStopped;

                IsRunning = true;
                _outputA.Play();
                _outputB.Play();
                _capture.StartRecording();
            }
            catch
            {
                StopCore();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void SetVolumes(float volumeA, float volumeB)
    {
        lock (_bufferGate)
        {
            if (_volumeA is not null) _volumeA.Volume = ClampVolume(volumeA);
            if (_volumeB is not null) _volumeB.Volume = ClampVolume(volumeB);
        }
    }

    public void Stop()
    {
        _lifecycleGate.Wait();
        try { StopCore(); }
        finally { _lifecycleGate.Release(); }
    }

    public async Task PlayCalibrationAsync(RoutingOptions options, CancellationToken cancellationToken = default)
    {
        ValidateOutputs(options);
        if (IsRunning)
            throw new InvalidOperationException("Stop streaming before running the calibration test.");

        using var enumerator = new MMDeviceEnumerator();
        using var deviceA = enumerator.GetDevice(options.OutputADeviceId);
        using var deviceB = enumerator.GetDevice(options.OutputBDeviceId);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var audioA = CreateCalibrationSignal(format, options.DelayAMilliseconds, options.VolumeA);
        var audioB = CreateCalibrationSignal(format, options.DelayBMilliseconds, options.VolumeB);
        using var streamA = new RawSourceWaveStream(new MemoryStream(audioA, writable: false), format);
        using var streamB = new RawSourceWaveStream(new MemoryStream(audioB, writable: false), format);
        using var playerA = CreatePlayer(deviceA, options.RenderLatencyMilliseconds);
        using var playerB = CreatePlayer(deviceB, options.RenderLatencyMilliseconds);

        playerA.Init(streamA);
        playerB.Init(streamB);
        playerA.Play();
        playerB.Play();

        var deadline = DateTime.UtcNow.AddSeconds(7);
        while ((playerA.PlaybackState != PlaybackState.Stopped || playerB.PlaybackState != PlaybackState.Stopped) &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(40, cancellationToken);
        }
    }

    private static WasapiPlayer CreatePlayer(MMDevice device, int latencyMilliseconds) =>
        new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(latencyMilliseconds)
            .WithMmcssThreadPriority("Audio")
            .WithCategory(AudioStreamCategory.Media)
            .Build();

    internal static void ValidateOutputs(RoutingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OutputADeviceId) || string.IsNullOrWhiteSpace(options.OutputBDeviceId))
            throw new InvalidOperationException("Select two output devices.");
        if (string.Equals(options.OutputADeviceId, options.OutputBDeviceId, StringComparison.Ordinal))
            throw new InvalidOperationException("Output A and Output B must be different devices.");
        if (options.DelayAMilliseconds is < 0 or > 2000 || options.DelayBMilliseconds is < 0 or > 2000)
            throw new InvalidOperationException("Calibration delays must be between 0 and 2000 milliseconds.");
    }

    internal static BufferedWaveProvider CreateBuffer(WaveFormat format, double delayMilliseconds)
    {
        var buffer = new BufferedWaveProvider(format, TimeSpan.FromSeconds(12))
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        var silenceLength = (int)(format.AverageBytesPerSecond * delayMilliseconds / 1000d);
        silenceLength -= silenceLength % format.BlockAlign;
        if (silenceLength > 0)
            buffer.AddSamples(new byte[silenceLength]);
        return buffer;
    }

    private void Capture_DataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (!IsRunning)
            return;
        if ((flags & AudioClientBufferFlags.Silent) == 0 && ContainsAudibleSignal(buffer))
            Volatile.Write(ref _lastAudioSignalTicks, DateTime.UtcNow.Ticks);
        lock (_bufferGate)
        {
            var droppedA = AddUnlessDrifted(_bufferA, _targetBufferAMilliseconds, buffer);
            var droppedB = AddUnlessDrifted(_bufferB, _targetBufferBMilliseconds, buffer);
            if (droppedA || droppedB)
                Interlocked.Increment(ref _driftDropCount);
        }
    }

    // An output slower than the capture clock lets its queue grow without bound; skip a packet to pull it back.
    internal static bool AddUnlessDrifted(BufferedWaveProvider? queue, double targetMilliseconds, ReadOnlySpan<byte> buffer)
    {
        if (queue is null)
            return false;
        if (queue.BufferedDuration.TotalMilliseconds > targetMilliseconds + MaxDriftMilliseconds)
            return true;
        queue.AddSamples(buffer);
        return false;
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!_stopping)
            RaiseFault(e.Exception?.Message ?? "System audio capture stopped responding.");
    }

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // StopCore unsubscribes before stopping, so any stop seen here is unexpected, with or without an error.
        if (_stopping)
            return;
        var name = ReferenceEquals(sender, _outputA) ? "Output A" : ReferenceEquals(sender, _outputB) ? "Output B" : "An output";
        RaiseFault(e.Exception?.Message ?? $"{name} stopped playing unexpectedly.");
    }

    private void RaiseFault(string message) => Faulted?.Invoke(this, message);

    private void StopCore()
    {
        _stopping = true;
        IsRunning = false;

        if (_capture is not null)
        {
            _capture.DataAvailable -= Capture_DataAvailable;
            _capture.RecordingStopped -= Capture_RecordingStopped;
            try { _capture.StopRecording(); } catch { }
        }
        if (_outputA is not null)
        {
            _outputA.PlaybackStopped -= Output_PlaybackStopped;
            try { _outputA.Stop(); } catch { }
        }
        if (_outputB is not null)
        {
            _outputB.PlaybackStopped -= Output_PlaybackStopped;
            try { _outputB.Stop(); } catch { }
        }

        _capture?.Dispose();
        _outputA?.Dispose();
        _outputB?.Dispose();
        _outputDeviceA?.Dispose();
        _outputDeviceB?.Dispose();
        _capture = null;
        _outputA = null;
        _outputB = null;
        _bufferA = null;
        _bufferB = null;
        _volumeA = null;
        _volumeB = null;
        _outputDeviceA = null;
        _outputDeviceB = null;
        Volatile.Write(ref _lastAudioSignalTicks, 0);
    }

    private static bool ContainsAudibleSignal(ReadOnlySpan<byte> buffer)
    {
        var samples = MemoryMarshal.Cast<byte, float>(buffer);
        for (var index = 0; index < samples.Length; index += 8)
        {
            if (Math.Abs(samples[index]) > 0.0015f)
                return true;
        }
        return false;
    }

    private static float ClampVolume(float volume) => Math.Clamp(volume, 0f, 1f);

    internal static byte[] CreateCalibrationSignal(WaveFormat format, int delayMilliseconds, float volume)
    {
        const int contentMilliseconds = 2400;
        var frameCount = (int)(format.SampleRate * (delayMilliseconds + contentMilliseconds) / 1000d);
        var samples = new float[frameCount * format.Channels];
        var pulseStarts = new[] { 350, 800, 1250, 1700 };
        var gain = 0.32f * ClampVolume(volume);

        foreach (var pulseStart in pulseStarts)
        {
            var startFrame = (int)(format.SampleRate * (delayMilliseconds + pulseStart) / 1000d);
            var pulseFrames = (int)(format.SampleRate * 0.025);
            for (var frame = 0; frame < pulseFrames && startFrame + frame < frameCount; frame++)
            {
                var envelope = 1f - frame / (float)pulseFrames;
                var sample = gain * envelope * (float)Math.Sin(2 * Math.PI * 950 * frame / format.SampleRate);
                for (var channel = 0; channel < format.Channels; channel++)
                    samples[(startFrame + frame) * format.Channels + channel] = sample;
            }
        }

        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public void Dispose()
    {
        Stop();
        _lifecycleGate.Dispose();
    }
}
