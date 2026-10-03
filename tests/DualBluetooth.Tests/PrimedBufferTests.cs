using DualBluetooth.Audio;
using NAudio.Wave;

namespace DualBluetooth.Tests;

public sealed class PrimedBufferTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    private static int BytesFor(int milliseconds) => Format.AverageBytesPerSecond * milliseconds / 1000;

    private static byte[] Loud(int milliseconds)
    {
        var bytes = new byte[BytesFor(milliseconds)];
        Array.Fill(bytes, (byte)0x7F);
        return bytes;
    }

    [Fact]
    public void Read_IsSilentUntilCushionIsBuilt()
    {
        var buffer = new PrimedBuffer(Format, 100);
        buffer.AddSamples(Loud(50));
        var output = new byte[BytesFor(10)];
        Array.Fill(output, (byte)1);

        Assert.Equal(output.Length, buffer.Read(output));

        Assert.All(output, b => Assert.Equal(0, b));
        Assert.Equal(0, buffer.UnderrunCount);
    }

    [Fact]
    public void Read_PlaysAudioOnceCushionIsBuilt()
    {
        var buffer = new PrimedBuffer(Format, 100);
        buffer.AddSamples(Loud(120));
        var output = new byte[BytesFor(10)];

        buffer.Read(output);

        Assert.All(output, b => Assert.Equal(0x7F, b));
    }

    [Fact]
    public void Read_UnderrunPadsWithSilence_CountsGap_AndRefillsCushionBeforeResuming()
    {
        var buffer = new PrimedBuffer(Format, 100);
        buffer.AddSamples(Loud(110));
        var output = new byte[BytesFor(20)];

        buffer.Read(new byte[BytesFor(100)]);          // drains most of it while primed
        buffer.Read(output);                           // only 10 ms left: partial audio, then silence
        Assert.Equal(1, buffer.UnderrunCount);
        Assert.Equal(0x7F, output[0]);
        Assert.Equal(0, output[^1]);

        buffer.AddSamples(Loud(30));                   // below the cushion: must stay silent
        var after = new byte[BytesFor(10)];
        buffer.Read(after);
        Assert.All(after, b => Assert.Equal(0, b));

        buffer.AddSamples(Loud(100));                  // cushion rebuilt: audio resumes
        buffer.Read(after);
        Assert.All(after, b => Assert.Equal(0x7F, b));
    }

    [Fact]
    public void Read_AlwaysFillsRequestedLength()
    {
        var buffer = new PrimedBuffer(Format, 100);

        Assert.Equal(1000 - 1000 % Format.BlockAlign, buffer.Read(new byte[1000 - 1000 % Format.BlockAlign]));
    }
}
