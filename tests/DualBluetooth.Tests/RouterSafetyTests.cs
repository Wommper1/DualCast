using DualBluetooth.Audio;
using NAudio.CoreAudioApi;

namespace DualBluetooth.Tests;

public sealed class RouterSafetyTests
{
    [Fact]
    public void SilentIfFlagged_PassesNormalPacketsThroughUntouched()
    {
        byte[] packet = [1, 2, 3, 4];
        var silence = Array.Empty<byte>();
        var result = DualAudioRouter.SilentIfFlagged(packet, AudioClientBufferFlags.None, ref silence);
        Assert.Equal(packet, result.ToArray());
        Assert.Empty(silence);
    }

    [Fact]
    public void SilentIfFlagged_ReplacesAFlaggedPacketWithZerosOfTheSameLength()
    {
        // WASAPI may leave stale bytes in a packet it flags silent; they must never reach the outputs.
        byte[] packet = [9, 9, 9, 9, 9, 9];
        var silence = Array.Empty<byte>();
        var result = DualAudioRouter.SilentIfFlagged(packet, AudioClientBufferFlags.Silent, ref silence);
        Assert.Equal(new byte[6], result.ToArray());
    }

    [Fact]
    public void SilentIfFlagged_StillFlagsSilenceWhenOtherFlagsAreSet()
    {
        byte[] packet = [7, 7, 7, 7];
        var silence = Array.Empty<byte>();
        var flags = AudioClientBufferFlags.Silent | AudioClientBufferFlags.DataDiscontinuity;
        Assert.Equal(new byte[4], DualAudioRouter.SilentIfFlagged(packet, flags, ref silence).ToArray());
    }

    [Fact]
    public void SilentIfFlagged_ReusesItsBufferAndGrowsForALargerPacket()
    {
        var silence = Array.Empty<byte>();
        DualAudioRouter.SilentIfFlagged(new byte[16], AudioClientBufferFlags.Silent, ref silence);
        var first = silence;
        DualAudioRouter.SilentIfFlagged(new byte[8], AudioClientBufferFlags.Silent, ref silence);
        Assert.Same(first, silence);
        var larger = DualAudioRouter.SilentIfFlagged(new byte[64], AudioClientBufferFlags.Silent, ref silence);
        Assert.Equal(64, larger.Length);
        Assert.All(larger.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var router = new DualAudioRouter();
        router.Dispose();
        router.Dispose();
    }

    [Fact]
    public void Stop_AfterDispose_DoesNothing()
    {
        // A late fault handler calls Stop after the window has already disposed the router on exit.
        var router = new DualAudioRouter();
        router.Dispose();
        router.Stop();
        Assert.False(router.IsRunning);
    }
}
