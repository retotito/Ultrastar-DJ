using Microsoft.Extensions.Logging;

using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Audio.Mics;

/// <summary>What to open for one player.</summary>
public sealed record MicSlot(int PlayerId, MicBinding Mic, double InputGain, double Threshold);

/// <summary>
/// Opens one input stream per physical device and fans its channels out to the players' pipelines
/// (two players on L/R of one SingStar dongle share a stream). Runs a 30 Hz analysis loop.
/// </summary>
public sealed class MicEngine : IDisposable
{
    private static readonly TimeSpan AnalyzePeriod = TimeSpan.FromMilliseconds(33);

    private readonly IAudioBackend _backend;
    private readonly ILogger<MicEngine> _log;
    private readonly Dictionary<int, MicPipeline> _pipelines = [];
    private readonly List<DeviceStream> _streams = [];
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MicEngine(IAudioBackend backend, ILogger<MicEngine> log)
    {
        _backend = backend;
        _log = log;
    }

    /// <summary>Raised from the analysis thread after every pass; handlers marshal to the UI themselves.</summary>
    public event Action<IReadOnlyList<PitchSample>>? Analyzed;

    /// <summary>Raised when a device's stream stopped on its own (unplugged).</summary>
    public event Action<string>? DeviceLost;

    public bool IsRunning => _loop is not null;
    public IReadOnlyCollection<MicPipeline> Pipelines => _pipelines.Values;
    public MicPipeline? Pipeline(int playerId) => _pipelines.GetValueOrDefault(playerId);

    /// <summary>Opens streams for the given slots (replacing any running set) and starts analysing.</summary>
    public void Start(IReadOnlyList<MicSlot> slots)
    {
        Stop();
        lock (_gate)
        {
            foreach (IGrouping<string, MicSlot> byDevice in slots.GroupBy(s => s.Mic.DeviceId))
            {
                AudioDeviceInfo? info = _backend.Find(byDevice.Key);
                if (info is null || !info.IsInput)
                {
                    _log.LogWarning("Mic device missing: {Device}", byDevice.Key);
                    continue;
                }

                List<MicPipeline> pipes = [];
                foreach (MicSlot slot in byDevice)
                {
                    MicPipeline p = new(slot.PlayerId, slot.Mic.Channel, info.DefaultSampleRateHz) { InputGain = slot.InputGain, Threshold = slot.Threshold };
                    _pipelines[slot.PlayerId] = p;
                    pipes.Add(p);
                }

                try
                {
                    DeviceStream ds = new(pipes);
                    ds.Line = _backend.OpenInput(info.Id, Math.Min(2, info.MaxInputChannels), null, ds.OnInput);
                    ds.DeviceId = info.Id;
                    _streams.Add(ds);
                }
                catch (AudioBackendException ex)
                {
                    _log.LogError(ex, "Cannot open mic device {Device}", info.Id);
                    foreach (MicPipeline p in pipes)
                    {
                        _pipelines.Remove(p.PlayerId);
                    }
                }
            }

            if (_streams.Count == 0)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => AnalyzeLoopAsync(_cts.Token));
            _log.LogInformation("Mic engine started: {Players} players on {Devices} devices", _pipelines.Count, _streams.Count);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
            foreach (DeviceStream ds in _streams)
            {
                ds.Line?.Dispose();
            }

            _streams.Clear();
            _pipelines.Clear();
        }

        if (cts is not null)
        {
            cts.Cancel();
            try
            {
                loop?.Wait(1000);
            }
            catch (AggregateException)
            {
            }

            cts.Dispose();
        }
    }

    private async Task AnalyzeLoopAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(AnalyzePeriod);
        List<PitchSample> samples = new(4);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                samples.Clear();
                lock (_gate)
                {
                    foreach (MicPipeline p in _pipelines.Values)
                    {
                        samples.Add(p.Analyze());
                    }

                    foreach (DeviceStream ds in _streams)
                    {
                        if (ds.Line is { IsActive: false } && !ds.LostReported)
                        {
                            ds.LostReported = true;
                            _log.LogWarning("Mic device stopped: {Device}", ds.DeviceId);
                            DeviceLost?.Invoke(ds.DeviceId);
                        }
                    }
                }

                Analyzed?.Invoke(samples);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose() => Stop();

    private sealed class DeviceStream(List<MicPipeline> pipelines)
    {
        // Sized for the largest block PortAudio hands us; 4096 frames is far above any default.
        private readonly float[] _scratch = new float[4096];

        public IAudioLine? Line { get; set; }
        public string DeviceId { get; set; } = "";
        public bool LostReported { get; set; }

        public void OnInput(ReadOnlySpan<float> interleaved, int frames, int channels)
        {
            if (frames > _scratch.Length)
            {
                frames = _scratch.Length;
            }

            foreach (MicPipeline p in pipelines)
            {
                p.Process(interleaved, frames, channels, _scratch);
            }
        }
    }
}
