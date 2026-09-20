namespace UltrastarDJ.Audio.Dsp;

/// <summary>
/// YIN pitch estimator (de Cheveigné &amp; Kawahara 2002) — a direct port of the algorithm used by the
/// prototype's <c>pitchy</c>. Allocation-free after construction; call from a worker thread, not the audio callback.
/// </summary>
public sealed class YinDetector
{
    private readonly float[] _diff;
    private readonly float[] _cmnd;
    private readonly int _tauMin;
    private readonly int _tauMax;

    /// <param name="windowSize">Analysis window in samples (2048 ≈ 46 ms @ 44.1 kHz).</param>
    /// <param name="sampleRateHz">Device rate.</param>
    /// <param name="minHz">Lowest detectable pitch (60 Hz covers bass voices).</param>
    /// <param name="maxHz">Highest detectable pitch (1200 Hz covers soprano + harmonics).</param>
    /// <param name="threshold">Absolute threshold on the normalised difference; lower = stricter (0.15 default).</param>
    public YinDetector(int windowSize, double sampleRateHz, double minHz = 60, double maxHz = 1200, float threshold = 0.15f)
    {
        WindowSize = windowSize;
        SampleRateHz = sampleRateHz;
        Threshold = threshold;
        _tauMin = Math.Max(2, (int)(sampleRateHz / maxHz));
        _tauMax = Math.Min(windowSize / 2, (int)(sampleRateHz / minHz));
        _diff = new float[_tauMax + 1];
        _cmnd = new float[_tauMax + 1];
    }

    public int WindowSize { get; }
    public double SampleRateHz { get; }
    public float Threshold { get; }

    /// <summary>Returns (frequencyHz, clarity 0..1). frequency is 0 when no periodicity is found.</summary>
    public (double FrequencyHz, double Clarity) Detect(ReadOnlySpan<float> window)
    {
        if (window.Length < WindowSize)
        {
            return (0, 0);
        }

        int half = WindowSize / 2;

        // Difference function d(τ) = Σ (x[i] − x[i+τ])² over the first half of the window.
        for (int tau = 0; tau <= _tauMax; tau++)
        {
            float sum = 0;
            for (int i = 0; i < half; i++)
            {
                float d = window[i] - window[i + tau];
                sum += d * d;
            }

            _diff[tau] = sum;
        }

        // Cumulative mean normalised difference d'(τ).
        _cmnd[0] = 1;
        float running = 0;
        for (int tau = 1; tau <= _tauMax; tau++)
        {
            running += _diff[tau];
            _cmnd[tau] = running == 0 ? 1 : _diff[tau] * tau / running;
        }

        // Absolute threshold: first τ below threshold, then walk to the local minimum.
        int best = -1;
        for (int tau = _tauMin; tau <= _tauMax; tau++)
        {
            if (_cmnd[tau] < Threshold)
            {
                while (tau + 1 <= _tauMax && _cmnd[tau + 1] < _cmnd[tau])
                {
                    tau++;
                }

                best = tau;
                break;
            }
        }

        if (best < 0)
        {
            // No dip under threshold: take the global minimum but report its (low) clarity so callers can reject it.
            float min = float.MaxValue;
            for (int tau = _tauMin; tau <= _tauMax; tau++)
            {
                if (_cmnd[tau] < min)
                {
                    min = _cmnd[tau];
                    best = tau;
                }
            }

            if (best < 0)
            {
                return (0, 0);
            }
        }

        // Parabolic interpolation around the minimum for sub-sample period accuracy.
        double refined = best;
        if (best > 0 && best < _tauMax)
        {
            float s0 = _cmnd[best - 1], s1 = _cmnd[best], s2 = _cmnd[best + 1];
            float denom = 2 * (2 * s1 - s2 - s0);
            if (Math.Abs(denom) > 1e-12f)
            {
                refined = best + (s2 - s0) / denom;
            }
        }

        double clarity = Math.Clamp(1 - _cmnd[best], 0, 1);
        return (SampleRateHz / refined, clarity);
    }
}
