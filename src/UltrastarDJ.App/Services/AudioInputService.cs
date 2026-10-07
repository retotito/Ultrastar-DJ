using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Audio.Monitor;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Owns the audio backend, the mic engine and the monitor mixer. Two modes: <b>test</b> (players panel open,
/// any subset of mics) and <b>game</b> (players assigned to open beamers, see <see cref="StartGameMics"/>).
/// Also tracks which mics are plugged in (docs/03-game-engine.md "Mic plug / unplug"): the device list is re-read
/// every few seconds while no stream is open (PortAudio cannot re-enumerate otherwise), and a stream that dies
/// reports its device as lost. A player only has a mic while it is plugged in: an unplugged mic is unassigned
/// (and with it the player leaves its beamer, see DisplayService); a plugged-in mic is offered, never auto-assigned.
/// </summary>
public sealed class AudioInputService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly PlayersService _players;
    private readonly NotificationService _notifications;
    private readonly ILogger<AudioInputService> _log;
    private readonly CancellationTokenSource _pollCts = new();
    // Input device ids currently plugged in. Replaced (never mutated) on the UI thread; the poll thread only reads it.
    private volatile IReadOnlySet<string> _present;
    private bool _gameMode;

    public AudioInputService(IAudioBackend backend, PlayersService players, NotificationService notifications, ILoggerFactory loggers)
    {
        Backend = backend;
        _players = players;
        _notifications = notifications;
        _log = loggers.CreateLogger<AudioInputService>();
        Mics = new MicEngine(backend, loggers.CreateLogger<MicEngine>());
        Monitor = new MonitorMixer(backend, loggers.CreateLogger<MonitorMixer>());
        Latency = new LatencyTest(backend, loggers.CreateLogger<LatencyTest>());
        players.Changed += OnPlayerChanged;
        Mics.DeviceLost += id => Dispatcher.UIThread.Post(() => OnDeviceLost(id));
        _present = InputIds();
        _ = Task.Run(() => PollAsync(_pollCts.Token));
    }

    /// <summary>UI thread: the device list or a mic's presence changed (panels re-read devices and missing marks).</summary>
    public event Action? MicsChanged;

    /// <summary>
    /// UI thread: a mic stopped while the game had it open. <see cref="PlaybackService"/> stops the song; the
    /// players are unassigned right after the handlers ran.
    /// </summary>
    public event Action<IReadOnlyList<PlayerConfig>>? GameMicLost;

    /// <summary>Startup: assigned mics that are not plugged in are unassigned, with one toast.</summary>
    public void ReleaseMissingMicsAtStartup()
    {
        IReadOnlyList<PlayerConfig> missing = MicPresence.Missing(_players.All, _present);
        if (missing.Count == 0)
        {
            return;
        }

        _notifications.Warn(
            missing.Count == 1 ? "Microphone not found" : $"{missing.Count} microphones not found",
            Unassigned(missing));
        Unassign(missing);
    }

    public IAudioBackend Backend { get; }
    public MicEngine Mics { get; }
    public MonitorMixer Monitor { get; }
    public LatencyTest Latency { get; }

    public IReadOnlyList<AudioDeviceInfo> InputDevices => Backend.Devices.Where(d => d.IsInput && !IsHidden(d)).ToList();
    public IReadOnlyList<AudioDeviceInfo> OutputDevices => Backend.Devices.Where(d => d.IsOutput && !IsHidden(d)).ToList();

    /// <summary>Re-enumerates devices; only works while nothing is open. Returns false if streams are running.</summary>
    public bool RefreshDevices() => Backend.RefreshDevices();

    /// <summary>Test mode: opens the given players' mics (replacing any running set). Restart-safe.</summary>
    public void StartTest(IEnumerable<PlayerConfig> players)
    {
        _gameMode = false;
        Mics.Start(players.Where(p => p.Mic is not null).Select(p => new MicSlot(p.Id, p.Mic!, p.InputGain, p.Threshold)).ToList());
    }

    /// <summary>Opens the singers' mics for a song. A mic lost now raises <see cref="GameMicLost"/> instead of a toast.</summary>
    public void StartGameMics(IReadOnlyList<MicSlot> slots)
    {
        _gameMode = true;
        Mics.Start(slots);
    }

    public void StopAll()
    {
        _gameMode = false;
        Monitor.Stop();
        Mics.Stop();
    }

    /// <summary>Routes the running mics to an output: the song's monitor, or the Audio Input test (where the Game Player's mute does not apply).</summary>
    public void StartMonitor(string outputDeviceId, int channelOffset = 0)
    {
        Monitor.Start(outputDeviceId, channelOffset, Mics.Pipelines);
        foreach (PlayerConfig p in _players.All)
        {
            Monitor.SetGain(p.Id, p.MixGain);
            Monitor.SetMuted(p.Id, p.MutedInMonitor(_gameMode));
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
        Monitor.SetMuted(p.Id, p.MutedInMonitor(_gameMode));
    }

    private HashSet<string> InputIds() => [.. InputDevices.Select(d => d.Id)];

    private async Task PollAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                // Refused while any stream is open (test, game, monitor, latency) — those report losses themselves.
                if (!Backend.RefreshDevices())
                {
                    continue;
                }

                HashSet<string> now = InputIds();
                MicPresence.Change change = MicPresence.Diff(_present, now);
                if (!change.IsEmpty)
                {
                    Dispatcher.UIThread.Post(() => ApplyChange(change, now));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyChange(MicPresence.Change change, IReadOnlySet<string> now)
    {
        _present = now;
        _log.LogInformation("Input devices changed: +[{Added}] -[{Removed}]", string.Join(", ", change.Added), string.Join(", ", change.Removed));

        IReadOnlyList<PlayerConfig> gone = MicPresence.On(_players.All, change.Removed);
        if (gone.Count > 0)
        {
            _notifications.Warn("Microphone disconnected", Unassigned(gone));
            Unassign(gone);
        }

        foreach (string id in change.Added)
        {
            _notifications.Info("Microphone connected", $"{id} — assign it to a player under Audio Input.");
        }

        MicsChanged?.Invoke();
    }

    private void OnDeviceLost(string deviceId)
    {
        _present = _present.Where(id => id != deviceId).ToHashSet();
        IReadOnlyList<PlayerConfig> affected = MicPresence.On(_players.All, [deviceId]);
        if (_gameMode && GameMicLost is not null)
        {
            GameMicLost(affected);
        }
        else
        {
            _notifications.Warn("Microphone disconnected", affected.Count > 0 ? Unassigned(affected) : deviceId);
        }

        Unassign(affected);
        MicsChanged?.Invoke();
    }

    private static string Unassigned(IReadOnlyList<PlayerConfig> players)
        => string.Join(", ", players.Select(MicPresence.Describe)) + " — unassigned. Plug it in and assign it under Audio Input.";

    private void Unassign(IEnumerable<PlayerConfig> players)
    {
        foreach (PlayerConfig p in players)
        {
            _players.Update(p.Id, c => c with { Mic = null });
        }
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
        _pollCts.Cancel();
        _players.Changed -= OnPlayerChanged;
        Monitor.Dispose();
        Mics.Dispose();
        Backend.Dispose();
        _log.LogDebug("Audio input service disposed");
    }
}
