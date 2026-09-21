using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Queue;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Songbook;

namespace UltrastarDJ.App.Services;

/// <summary>A guest's wish, shown in the queue widget until the DJ queues or dismisses it.</summary>
public sealed record SongRequest(Song Song, string Guest, DateTime At);

/// <summary>
/// Guest songbook: hosts <see cref="SongbookServer"/>, feeds it library/queue state and collects requests.
/// Settings (port, PIN, autostart) are persisted.
/// </summary>
public sealed class SongbookService : ISongbookBackend, IAsyncDisposable
{
    private const string SettingsName = "songbook";
    private const int RequestLimit = 50;

    private readonly LibraryService _library;
    private readonly PlaybackService _playback;
    private readonly Playlist _playlist;
    private readonly NotificationService _notifications;
    private readonly ISettingsStore _settings;
    private readonly ILogger<SongbookService> _log;
    private readonly SongbookServer _server;
    private SongbookDocument _doc;

    public SongbookService(LibraryService library, PlaybackService playback, Playlist playlist, NotificationService notifications,
        ISettingsStore settings, ILoggerFactory loggers)
    {
        _library = library;
        _playback = playback;
        _playlist = playlist;
        _notifications = notifications;
        _settings = settings;
        _log = loggers.CreateLogger<SongbookService>();
        _doc = settings.Load(SettingsName, SongbookDocument.Default());
        _server = new SongbookServer(this, loggers.CreateLogger<SongbookServer>()) { Pin = _doc.PinEnabled ? _doc.Pin : null };
    }

    /// <summary>Raised on the UI thread.</summary>
    public event Action? Changed;

    public bool IsRunning => _server.IsRunning;
    public int Port => _doc.Port;
    public bool PinEnabled => _doc.PinEnabled;
    public string Pin => _doc.Pin;
    public bool AutoStart => _doc.AutoStart;
    public string? LastError { get; private set; }
    public ObservableCollection<SongRequest> Requests { get; } = [];

    /// <summary>http://&lt;lan-ip&gt;:port for every active IPv4 interface — what guests type or scan.</summary>
    public IReadOnlyList<string> Urls()
    {
        List<string> urls = [];
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    urls.Add($"http://{ip.Address}:{_doc.Port}");
                }
            }
        }

        return urls;
    }

    public async Task StartAsync()
    {
        LastError = null;
        try
        {
            await _server.StartAsync(_doc.Port).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
        {
            LastError = $"Cannot listen on port {_doc.Port}: {ex.Message}";
            _log.LogWarning(ex, "Songbook start failed");
        }

        Notify();
    }

    public async Task StopAsync()
    {
        await _server.StopAsync().ConfigureAwait(false);
        Notify();
    }

    public Task AutoStartAsync() => _doc.AutoStart ? StartAsync() : Task.CompletedTask;

    public void SetAutoStart(bool on) => Save(_doc with { AutoStart = on });

    public void SetPinEnabled(bool on)
    {
        Save(_doc with { PinEnabled = on });
        _server.Pin = on ? _doc.Pin : null;
    }

    public void RegeneratePin()
    {
        Save(_doc with { Pin = NewPin() });
        _server.Pin = _doc.PinEnabled ? _doc.Pin : null;
    }

    /// <summary>Takes effect on the next start.</summary>
    public void SetPort(int port) => Save(_doc with { Port = Math.Clamp(port, 1024, 65535) });

    public void Dismiss(SongRequest request)
    {
        Requests.Remove(request);
        Notify();
    }

    public void Accept(SongRequest request)
    {
        _playlist.Add(request.Song);
        Dismiss(request);
    }

    // ── ISongbookBackend (Kestrel threads) ─────────────────────────────

    public IReadOnlyList<SongbookSong> Search(string query, int limit)
    {
        string q = query.Trim();
        if (q.Length < 2)
        {
            return [];
        }

        return _library.Songs
            .Where(s => _library.IsAvailable(s.SourceId) && (s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Artist.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(ToGuestSong)
            .ToList();
    }

    public SongbookState State()
    {
        Song? now = _playback.State is PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused ? _playback.Song : null;
        int active = _playlist.ActiveIndex;
        IReadOnlyList<SongbookSong> queue = _playlist.Items.Skip(active + 1).Take(5).Select(ToGuestSong).ToList();
        return new SongbookState(now is null ? null : ToGuestSong(now), queue, _library.Songs.Count);
    }

    public bool Request(string songId, string guestName)
    {
        Song? song = _library.Songs.FirstOrDefault(s => s.Id == songId);
        if (song is null)
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (Requests.Count >= RequestLimit)
            {
                Requests.RemoveAt(0);
            }

            Requests.Add(new SongRequest(song, guestName, DateTime.Now));
            _notifications.Info($"{guestName} wants to sing", $"{song.Artist} – {song.Title}");
            Changed?.Invoke();
        });
        return true;
    }

    private SongbookSong ToGuestSong(Song s) => new(s.Id, s.Artist, s.Title, s.Year, s.Language, _library.SourceLabel(s.SourceId));

    private void Save(SongbookDocument doc)
    {
        _doc = doc;
        _settings.Save(SettingsName, _doc);
        Notify();
    }

    private void Notify()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
        }
    }

    private static string NewPin() => RandomNumberGenerator.GetInt32(0, 10000).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    public sealed record SongbookDocument(int Port, bool PinEnabled, string Pin, bool AutoStart)
    {
        public static SongbookDocument Default() => new(4747, false, NewPin(), false);
    }
}
