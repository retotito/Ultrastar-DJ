namespace UltrastarDJ.Audio.Dsp;

/// <summary>
/// Single-producer / single-consumer float ring. The audio callback writes, a worker reads.
/// <see cref="Write"/> never blocks; overrun simply drops the oldest samples.
/// </summary>
public sealed class SampleRing
{
    private readonly float[] _buf;
    private long _written; // total samples ever written; read by consumer via Volatile

    public SampleRing(int capacity)
    {
        _buf = new float[capacity];
    }

    public int Capacity => _buf.Length;
    public long TotalWritten => Volatile.Read(ref _written);

    /// <summary>Producer side (audio thread).</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        long w = _written;
        int pos = (int)(w % _buf.Length);
        int first = Math.Min(samples.Length, _buf.Length - pos);
        samples[..first].CopyTo(_buf.AsSpan(pos, first));
        if (first < samples.Length)
        {
            samples[first..].CopyTo(_buf.AsSpan(0, samples.Length - first));
        }

        Volatile.Write(ref _written, w + samples.Length);
    }

    /// <summary>Consumer side: copies the most recent <paramref name="destination"/>.Length samples. Returns false if not enough data yet.</summary>
    public bool ReadLatest(Span<float> destination)
    {
        long w = Volatile.Read(ref _written);
        int n = destination.Length;
        if (n > _buf.Length || w < n)
        {
            return false;
        }

        int end = (int)(w % _buf.Length);
        int start = end - n;
        if (start >= 0)
        {
            _buf.AsSpan(start, n).CopyTo(destination);
        }
        else
        {
            int tail = -start;
            _buf.AsSpan(_buf.Length - tail, tail).CopyTo(destination);
            _buf.AsSpan(0, end).CopyTo(destination[tail..]);
        }

        return true;
    }

    /// <summary>Consumer side for streaming reads (monitor mixer): copies samples starting at absolute index <paramref name="from"/>.</summary>
    public int ReadFrom(long from, Span<float> destination)
    {
        long w = Volatile.Read(ref _written);
        long available = w - from;
        if (available <= 0)
        {
            return 0;
        }

        if (available > _buf.Length)
        {
            // Consumer fell behind by more than the ring: skip to the oldest retained sample.
            from = w - _buf.Length;
            available = _buf.Length;
        }

        int n = (int)Math.Min(available, destination.Length);
        int start = (int)(from % _buf.Length);
        int first = Math.Min(n, _buf.Length - start);
        _buf.AsSpan(start, first).CopyTo(destination);
        if (first < n)
        {
            _buf.AsSpan(0, n - first).CopyTo(destination[first..]);
        }

        return n;
    }
}
