using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Audio.Monitor;

/// <summary>
/// Measures a player's total latency the way the game uses it: an A4 tone on the game output → speaker → air → mic →
/// USB → the same pitch analysis as in the game (window, 30 Hz analysis, median) → the moment it reports A4.
/// The old version detected a beep's onset in the raw signal and so missed the analysis (~60 ms, see
/// PitchPathDelayTests). The result includes the output latency of <c>outputDeviceId</c>; the game subtracts that
/// device's latency setting (Core.Timing.LatencyModel).
/// </summary>
public sealed class LatencyTest(IAudioBackend backend, ILogger<LatencyTest> log)
{
    // A4 = MIDI 69: a clean, mid-range note every mic and the YIN detector handle well.
    private const double ToneHz = 440;
    private const double ToneMidi = 69;
    private const double ToneSec = 0.5;
    private const double TrialTimeoutSec = 1.5;
    private const double GapBetweenTrialsSec = 0.7;
    private static readonly TimeSpan AnalyzePeriod = TimeSpan.FromMilliseconds(33); // as MicEngine
    // Output + analysis alone exceed 30 ms; slower than 1.2 s is not this tone.
    private const double MinPlausibleMs = 30;
    private const double MaxPlausibleMs = 1200;

    public sealed record Result(IReadOnlyList<double> TrialsMs, double MedianMs);

    /// <param name="outputDeviceId">Where the tone plays: the game output, so its latency is part of the result.</param>
    /// <param name="mic">The player's mic.</param>
    /// <param name="inputGain">The player's gain, so quiet mics are analysed as in the game.</param>
    /// <param name="trials">Odd numbers give a clean median.</param>
    /// <param name="progress">Reports trial results as they complete.</param>
    /// <param name="ct">Cancels between trials.</param>
    public async Task<Result> RunAsync(string outputDeviceId, MicBinding mic, double inputGain = 1, int trials = 5, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        AudioDeviceInfo inInfo = backend.Find(mic.DeviceId) ?? throw new AudioBackendException($"Mic device not found: {mic.DeviceId}");
        AudioDeviceInfo outInfo = backend.Find(outputDeviceId) ?? throw new AudioBackendException($"Output device not found: {outputDeviceId}");

        // No gate: a quiet room must not hide the tone. Gain as in the game.
        MicPipeline pipe = new(0, mic.Channel, inInfo.DefaultSampleRateHz) { InputGain = inputGain, Threshold = 0 };
        float[] scratch = new float[8192];
        Tone tone = new(outInfo.DefaultSampleRateHz);
        List<double> results = [];

        using IAudioLine input = backend.OpenInput(inInfo.Id, Math.Min(2, inInfo.MaxInputChannels), null,
            (samples, frames, channels) => pipe.Process(samples, frames, channels, scratch));
        using IAudioLine output = backend.OpenOutput(outInfo.Id, 0, null, tone.OnOutput);
        await Task.Delay(500, ct).ConfigureAwait(false);

        for (int t = 0; t < trials; t++)
        {
            ct.ThrowIfCancellationRequested();
            pipe.Reset();
            await AnalyzeUntilAsync(pipe, silence: true, TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);

            long trigger = tone.Trigger();
            double? ms = await AnalyzeUntilAsync(pipe, silence: false, TimeSpan.FromSeconds(TrialTimeoutSec), ct).ConfigureAwait(false) is { } at
                ? Stopwatch.GetElapsedTime(trigger, at).TotalMilliseconds
                : null;
            if (ms is null)
            {
                log.LogWarning("Latency trial {Trial}: the tone was not recognised as A4 (level {Level:F4})", t + 1, pipe.LevelRms);
            }
            else if (ms is < MinPlausibleMs or > MaxPlausibleMs)
            {
                log.LogWarning("Latency trial {Trial}: {Ms:F0} ms rejected as implausible", t + 1, ms);
            }
            else
            {
                results.Add(ms.Value);
                progress?.Report(ms.Value);
                log.LogInformation("Latency trial {Trial}: {Ms:F0} ms (tone → recognised A4)", t + 1, ms);
            }

            await Task.Delay(TimeSpan.FromSeconds(GapBetweenTrialsSec), ct).ConfigureAwait(false);
        }

        if (results.Count == 0)
        {
            throw new AudioBackendException("The calibration tone was not recognised by the microphone. Raise the speaker volume or the mic gain, or move the mic closer.");
        }

        double[] sorted = [.. results.Order()];
        return new Result(results, sorted[sorted.Length / 2]);
    }

    /// <summary>
    /// Runs the analysis at the game's rate until the pipeline reports the tone (or, with <paramref name="silence"/>,
    /// no longer reports it). Returns the Stopwatch timestamp of that analysis, or null on timeout.
    /// </summary>
    private static async Task<long?> AnalyzeUntilAsync(MicPipeline pipe, bool silence, TimeSpan timeout, CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        using PeriodicTimer timer = new(AnalyzePeriod);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            pipe.Analyze();
            bool isTone = Math.Abs(pipe.LastMidiNote - ToneMidi) < 0.5;
            if (isTone != silence)
            {
                return Stopwatch.GetTimestamp();
            }

            if (Stopwatch.GetElapsedTime(start) > timeout)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>A4 sine with 5 ms fades (no click that could look like an onset), started by <see cref="Trigger"/>.</summary>
    private sealed class Tone(double rate)
    {
        private readonly int _len = (int)(rate * ToneSec);
        private readonly int _fade = (int)(rate * 0.005);
        private int _pos = int.MaxValue;

        /// <returns>Stopwatch timestamp of the trigger: the measured latency starts here.</returns>
        public long Trigger()
        {
            long now = Stopwatch.GetTimestamp();
            Volatile.Write(ref _pos, 0);
            return now;
        }

        public void OnOutput(Span<float> interleaved, int frames, int channels)
        {
            int pos = Volatile.Read(ref _pos);
            if (pos >= _len)
            {
                return;
            }

            for (int i = 0; i < frames && pos < _len; i++, pos++)
            {
                float env = Math.Min(1f, Math.Min(pos, _len - pos) / (float)_fade);
                float v = 0.8f * env * (float)Math.Sin(2 * Math.PI * ToneHz * pos / rate);
                interleaved[i * channels] += v;
                if (channels > 1)
                {
                    interleaved[i * channels + 1] += v;
                }
            }

            Volatile.Write(ref _pos, pos);
        }
    }
}
