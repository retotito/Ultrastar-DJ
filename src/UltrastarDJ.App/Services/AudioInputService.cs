using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Audio.Monitor;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Owns the audio backend, the mic engine and the monitor mixer. Two modes: <b>test</b> (players panel open,
/// any subset of mics) and, from Sprint 4, <b>game</b> (players assigned to open beamers).
/// </summary>
public sealed class AudioInputService : IDisposable
{
    private readonly PlayersService _players;
    private readonly ILogger<AudioInputService> _log;

    public AudioInputService(IAudioBackend backend, PlayersService players, ILoggerFactory loggers)
    {
        Backend = backend;
        _players = players;
        _log = loggers.CreateLogger<AudioInputService>();
        Mics = new MicEngine(backend, loggers.CreateLogger<MicEngine>());
        Monitor = new MonitorMixer(backend, loggers.CreateLogger<MonitorMixer>());
        Latency = new LatencyTest(backend, loggers.CreateLogger<LatencyTest>());
        players.Changed += OnPlayerChanged;
    }

    public IAudioBackend Backend { get; }
    public MicEngine Mics { get; }
    public MonitorMixer Monitor { get; }
    public LatencyTest Latency { get; }

    public IReadOnlyList<AudioDeviceInfo> InputDevices => Backend.Devices.Where(d => d.IsInput && !IsHidden(d)).ToList();
    public IReadOnlyList<AudioDeviceInfo> OutputDevices => Backend.Devices.Where(d => d.IsOutput && !IsHidden(d)).ToList();

    /// <summary>Re-enumerates devices; only works while nothing is open. Returns false if streams are running.</summary>
    public bool RefreshDevices() => Backend.RefreshDevices();

    /// <summary>Opens every player that has a mic bound (test mode). Restart-safe.</summary>
    public void StartAll()
    {
        List<MicSlot> slots = _players.WithMic
            .Select(p => new MicSlot(p.Id, p.Mic!, p.InputGain, p.Threshold))
            .ToList();
        Mics.Start(slots);
    }

    public void StopAll()
    {
        Monitor.Stop();
        Mics.Stop();
    }

    /// <summary>Routes the running mics to an output (test mode monitoring).</summary>
    public void StartMonitor(string outputDeviceId, int channelOffset = 0)
    {
        Monitor.Start(outputDeviceId, channelOffset, Mics.Pipelines);
        foreach (PlayerConfig p in _players.All)
        {
            Monitor.SetGain(p.Id, p.MixGain);
        }
    }

    public void StopMonitor() => Monitor.Stop();

    private void OnPlayerChanged(PlayerConfig p)
    {
        // Live-apply the knobs that do not need a stream restart.
        if (Mics.Pipeline(p.Id) is { } pipe)
        {
            pipe.InputGain = p.InputGain;
            pipe.Threshold = p.Threshold;
        }

        Monitor.SetGain(p.Id, p.MixGain);
    }

    private static bool IsHidden(AudioDeviceInfo d)
    {
        string n = d.Name.ToLowerInvariant();
        return n.Contains("blackhole") || n.Contains("loopback") || n.Contains("audio recorder") || n.Contains("screen recorder");
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _players.Changed -= OnPlayerChanged;
        Monitor.Dispose();
        Mics.Dispose();
        Backend.Dispose();
        _log.LogDebug("Audio input service disposed");
    }
}
