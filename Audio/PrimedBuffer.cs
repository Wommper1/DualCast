using NAudio.Wave;

namespace DualBluetooth.Audio;

/// <summary>
/// Jitter buffer between capture and one output. Output stays silent until a cushion of audio has built up,
/// and after an underrun it goes silent again until the cushion refills, instead of stuttering at an empty queue.
/// The cushion also carries the per-output sync delay.
/// </summary>
public sealed class PrimedBuffer : IWaveProvider
{
    private readonly BufferedWaveProvider _queue;
    private readonly int _primeBytes;
    private bool _primed;
    private long _underruns;

    public PrimedBuffer(WaveFormat format, double cushionMilliseconds)
    {
        WaveFormat = format;
        _queue = new BufferedWaveProvider(format, TimeSpan.FromSeconds(12))
        {
            DiscardOnBufferOverflow = true,
            ReadFully = false
        };
        _primeBytes = (int)(format.AverageBytesPerSecond * cushionMilliseconds / 1000d);
        _primeBytes -= _primeBytes % format.BlockAlign;
    }

    public WaveFormat WaveFormat { get; }

    public TimeSpan BufferedDuration => _queue.BufferedDuration;

    /// <summary>Times the output ran out of audio and had to re-fill its cushion.</summary>
    public long UnderrunCount => Interlocked.Read(ref _underruns);

    public void AddSamples(ReadOnlySpan<byte> samples) => _queue.AddSamples(samples);

    public int Read(Span<byte> buffer)
    {
        if (!_primed)
        {
            if (_queue.BufferedBytes < _primeBytes)
            {
                buffer.Clear();
                return buffer.Length;
            }
            _primed = true;
        }

        var read = _queue.Read(buffer);
        if (read < buffer.Length)
        {
            buffer[read..].Clear();
            _primed = false;
            Interlocked.Increment(ref _underruns);
        }
        return buffer.Length;
    }
}
