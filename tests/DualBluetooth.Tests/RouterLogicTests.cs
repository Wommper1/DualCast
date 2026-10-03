using DualBluetooth.Audio;
using NAudio.Wave;

namespace DualBluetooth.Tests;

public sealed class RouterLogicTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

    private static RoutingOptions Options(string a = "a", string b = "b", int delayA = 0, int delayB = 0) =>
        new(a, b, delayA, delayB, 1f, 1f, 100);

    [Theory]
    [InlineData("", "b")]
    [InlineData("a", " ")]
    [InlineData("same", "same")]
    public void ValidateOutputs_RejectsMissingOrIdenticalDevices(string a, string b) =>
        Assert.Throws<InvalidOperationException>(() => DualAudioRouter.ValidateOutputs(Options(a, b)));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 2001)]
    public void ValidateOutputs_RejectsOutOfRangeDelay(int delayA, int delayB) =>
        Assert.Throws<InvalidOperationException>(() => DualAudioRouter.ValidateOutputs(Options(delayA: delayA, delayB: delayB)));

    [Fact]
    public void ValidateOutputs_AcceptsBoundaryDelays() =>
        DualAudioRouter.ValidateOutputs(Options(delayA: 0, delayB: 2000));

    private static float[] Samples(byte[] bytes)
    {
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }

    private static int FirstNonZeroFrame(float[] samples)
    {
        for (var i = 0; i < samples.Length; i += 2)
            if (samples[i] != 0f)
                return i / 2;
        return -1;
    }

    [Fact]
    public void CalibrationSignal_LengthCoversDelayPlusContent()
    {
        var bytes = DualAudioRouter.CreateCalibrationSignal(Format, delayMilliseconds: 100, volume: 1f);

        Assert.Equal(48_000 * 2.5 * Format.BlockAlign, bytes.Length);
    }

    [Fact]
    public void CalibrationSignal_DelayShiftsFirstPulseByExactFrames()
    {
        var plain = FirstNonZeroFrame(Samples(DualAudioRouter.CreateCalibrationSignal(Format, 0, 1f)));
        var delayed = FirstNonZeroFrame(Samples(DualAudioRouter.CreateCalibrationSignal(Format, 100, 1f)));

        Assert.Equal(48_000 * 350 / 1000 + 1, plain); // frame 0 of a sine pulse is exactly zero
        Assert.Equal(4_800, delayed - plain);
    }

    [Fact]
    public void CalibrationSignal_PeakScalesWithVolume_AndMutesAtZero()
    {
        var full = Samples(DualAudioRouter.CreateCalibrationSignal(Format, 0, 1f)).Max(Math.Abs);
        var half = Samples(DualAudioRouter.CreateCalibrationSignal(Format, 0, 0.5f)).Max(Math.Abs);
        var muted = Samples(DualAudioRouter.CreateCalibrationSignal(Format, 0, 0f)).Max(Math.Abs);

        Assert.InRange(full, 0.25f, 0.32f);
        Assert.Equal(full / 2, half, precision: 4);
        Assert.Equal(0f, muted);
    }

    [Fact]
    public void CalibrationSignal_ContainsFourPulses()
    {
        var samples = Samples(DualAudioRouter.CreateCalibrationSignal(Format, 0, 1f));
        var pulses = 0;
        var inPulse = false;
        var silentRun = 0;
        for (var i = 0; i < samples.Length; i += 2)
        {
            if (samples[i] != 0f) { if (!inPulse) pulses++; inPulse = true; silentRun = 0; }
            else if (++silentRun > 200) inPulse = false; // sine zero crossings are short; gaps between pulses are long
        }

        Assert.Equal(4, pulses);
    }

    [Fact]
    public void AddUnlessDrifted_AddsWhileWithinTolerance_ThenDropsAndHoldsLevel()
    {
        var buffer = new PrimedBuffer(Format, 100);
        var packet = new byte[(int)(Format.AverageBytesPerSecond * 0.010)];

        Assert.False(DualAudioRouter.AddUnlessDrifted(buffer, 100, packet));
        Assert.Equal(10, buffer.BufferedDuration.TotalMilliseconds, precision: 1);

        while (buffer.BufferedDuration.TotalMilliseconds <= 100 + DualAudioRouter.MaxDriftMilliseconds)
            buffer.AddSamples(packet);
        var full = buffer.BufferedDuration.TotalMilliseconds;

        Assert.True(DualAudioRouter.AddUnlessDrifted(buffer, 100, packet));
        Assert.Equal(full, buffer.BufferedDuration.TotalMilliseconds);
    }

    [Fact]
    public void AddUnlessDrifted_NullQueue_IsNoOp() =>
        Assert.False(DualAudioRouter.AddUnlessDrifted(null, 100, new byte[8]));
}
