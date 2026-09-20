namespace UltrastarDJ.Core.Game;

/// <summary>
/// Circular buffer of MIDI notes with median smoothing — rejects one-off spikes and dropouts that a
/// running mean would let through. -1 means "no pitch".
/// </summary>
public sealed class PitchRingBuffer
{
    private readonly double[] _buf;
    private readonly double[] _scratch;
    private int _ptr;
    private int _count;

    public PitchRingBuffer(int size = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        _buf = new double[size];
        _scratch = new double[size];
        Array.Fill(_buf, -1);
    }

    public int Size => _buf.Length;

    public void Push(double midiNote)
    {
        _buf[_ptr] = midiNote;
        _ptr = (_ptr + 1) % _buf.Length;
        if (_count < _buf.Length)
        {
            _count++;
        }
    }

    /// <summary>Median of the valid (≥ 0) samples currently held, or -1 if none. Allocation-free.</summary>
    public double Median()
    {
        int n = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_buf[i] >= 0)
            {
                _scratch[n++] = _buf[i];
            }
        }

        if (n == 0)
        {
            return -1;
        }

        Array.Sort(_scratch, 0, n);
        int mid = n / 2;
        return n % 2 == 0 ? (_scratch[mid - 1] + _scratch[mid]) / 2 : _scratch[mid];
    }

    public void Reset()
    {
        Array.Fill(_buf, -1);
        _ptr = 0;
        _count = 0;
    }
}
