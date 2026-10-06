using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Queue;
using UltrastarDJ.Core.Songbook;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Songbook;

namespace UltrastarDJ.App.Services;

/// <summary>A guest's wish, shown in the queue widget until the DJ queues or dismisses it.</summary>
public sealed record SongRequest(string Id, Song Song, string Guest, DateTime At);

/// <summary>
/// Guest songbook: hosts <see cref="SongbookServer"/>, feeds it library/queue state and collects requests.
/// Settings (port, PIN, autostart) are persisted.
/// </summary>
public sealed class SongbookService : ISongbookBackend, IAsyncDisposable
{
    private const string SettingsName = "songbook";

    private readonly LibraryService _library;
    private readonly UsdbService _usdb;
    private readonly PlaybackService _playback;
    private readonly Playlist _playlist;
    private readonly NotificationService _notifications;
    private readonly ISettingsStore _settings;
    private readonly ILogger<SongbookService> _log;
    private readonly SongbookServer _server;
    private SongbookDocument _doc;

    public SongbookService(LibraryService library, PlaybackService playback, Playlist playlist, NotificationService notifications,
        ISettingsStore settings, UsdbService usdb, ILoggerFactory loggers)
    {
        _library = library;
        _usdb = usdb;
        library.Changed += OnLibraryChanged;
        library.AvailabilityChanged += OnLibraryChanged;
        _playback = playback;
        _playlist = playlist;
        // Remember which accepted requests went on stage (their status turns "sung" when the queue moves on).
        playlist.Changed += () =>
        {
            _requests.Observe(QueueIds(), _playlist.ActiveIndex);
            Changed?.Invoke();
        };
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

    /// <summary>
    /// The one address for the beamer's QR code: the Wi-Fi one guests' phones can reach (Core.Songbook.SongbookAddress),
    /// or null when the songbook is off or the Mac has no usable network.
    /// </summary>
    public string? GuestUrl()
    {
        if (!IsRunning)
        {
            return null;
        }

        List<LocalAddress> addresses = [];
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            bool wireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            bool ethernet = nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet;
            foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    addresses.Add(new LocalAddress(ip.Address.ToString(), wireless, ethernet));
                }
            }
        }

        return SongbookAddress.Best(addresses) is { } best ? $"http://{best}:{_doc.Port}" : null;
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

    // ── Requests (UI thread): the book decides, the REQUESTS list shows the waiting ones ──
    private readonly GuestRequests _requests = new();

    public void Dismiss(SongRequest request)
    {
        _requests.Dismiss(request.Id);
        RefreshRequests();
    }

    public void Accept(SongRequest request)
    {
        _requests.Accept(request.Id);
        _playlist.Add(request.Song);
        RefreshRequests();
    }

    /// <summary>The guest who asked for a queued or on-stage song — the queue shows it so the DJ can call them up.</summary>
    public string? RequesterOf(string songId) => _requests.RequesterOf(songId, QueueIds(), _playlist.ActiveIndex);

    private IReadOnlyList<string> QueueIds() => [.. _playlist.Items.Select(s => s.Id)];

    private void RefreshRequests()
    {
        Requests.Clear();
        foreach (GuestRequest r in _requests.Waiting)
        {
            if (_library.Songs.FirstOrDefault(s => s.Id == r.SongId) is { } song)
            {
                Requests.Add(new SongRequest(r.Id, song, r.Guest, r.At));
            }
        }

        Notify();
    }

    // ── ISongbookBackend (Kestrel threads) ─────────────────────────────

    // ── Catalog for the phones (they search and filter it themselves) ──
    private int _libraryVersion = 1;

    public int LibraryVersion => Volatile.Read(ref _libraryVersion);

    // Library changed, or a source came or went: phones fetch the catalog again on their next status check.
    private void OnLibraryChanged() => Interlocked.Increment(ref _libraryVersion);

    /// <summary>Only what can be played now — no songs from unplugged drives, no USDB while offline.</summary>
    public SongbookCatalog Catalog() => SongbookCatalog.Build(_library.Songs.Where(s => _library.IsAvailable(s.SourceId)));

    public async Task<string?> YouTubeIdAsync(string songId, CancellationToken ct)
    {
        Song? song = _library.Songs.FirstOrDefault(s => s.Id == songId);
        if (song is null)
        {
            return null;
        }

        if (song.HasYouTube)
        {
            return song.YouTubeId;
        }

        if (song.UsdbId is not { } usdbId)
        {
            return null;
        }

        try
        {
            string txt = await _usdb.GetSongTxtAsync(usdbId, song.UsdbMtime, ct).ConfigureAwait(false);
            return UltraStarParser.ParseHeader(txt).YouTubeId;
        }
        catch (Exception ex) when (ex is UsdbException or HttpRequestException or IOException)
        {
            _log.LogInformation("Songbook preview: no YouTube id for {Song} ({Error})", songId, ex.Message);
            return null;
        }
    }

    // Everything below reads or changes the queue and the request book: done on the UI thread, which owns both.
    public Task<SongbookState> StateAsync(string? clientId) => Dispatcher.UIThread.InvokeAsync(() => BuildState(clientId)).GetTask();

    private SongbookState BuildState(string? clientId)
    {
        Song? now = _playback.State is PlaybackState.Countdown or PlaybackState.Playing or PlaybackState.Paused ? _playback.Song : null;
        IReadOnlyList<string> ids = QueueIds();
        int active = _playlist.ActiveIndex;
        IReadOnlyList<SongbookSong> queue = _playlist.Items.Skip(active + 1).Take(5).Select(ToGuestSong).ToList();

        // Songs nobody should request again right now: on stage, queued, or waiting for the DJ.
        Dictionary<string, string> taken = [];
        foreach (GuestRequest r in _requests.Waiting)
        {
            taken[r.SongId] = "Requested";
        }

        for (int i = Math.Max(0, active); i < ids.Count; i++)
        {
            taken[ids[i]] = i == active ? "On stage" : i == active + 1 ? "Up next" : $"In the queue #{i - active}";
        }

        List<SongbookMine> mine = [];
        if (clientId is not null)
        {
            foreach (GuestRequest r in _requests.Of(clientId).Reverse())
            {
                GuestRequestStatus status = GuestRequests.StatusOf(r, ids, active, out int position);
                Song? song = _library.Songs.FirstOrDefault(s => s.Id == r.SongId);
                mine.Add(new SongbookMine(r.Id, r.SongId, song?.Title ?? "", song?.Artist ?? "", StatusText(status, position), status == GuestRequestStatus.Waiting));
            }
        }

        return new SongbookState(now is null ? null : ToGuestSong(now), queue, _library.Songs.Count, LibraryVersion, mine, taken);
    }

    private static string StatusText(GuestRequestStatus status, int position) => status switch
    {
        GuestRequestStatus.Waiting => "Waiting for the DJ",
        GuestRequestStatus.Queued => position == 1 ? "Up next" : $"In the queue #{position}",
        GuestRequestStatus.OnStage => "On stage now 🎤",
        GuestRequestStatus.Sung => "Sung ✓",
        _ => "Not this time",
    };

    public Task<SongbookRequestResult> RequestAsync(string songId, string guestName, string clientId) => Dispatcher.UIThread.InvokeAsync(() =>
    {
        Song? song = _library.Songs.FirstOrDefault(s => s.Id == songId);
        if (song is null || !_library.IsAvailable(song.SourceId))
        {
            return new SongbookRequestResult(false, true, null);
        }

        RequestOutcome outcome = _requests.Add(songId, guestName, clientId, DateTime.Now, QueueIds(), _playlist.ActiveIndex);
        if (outcome.Request is null)
        {
            return new SongbookRequestResult(false, false, outcome.Refusal);
        }

        RefreshRequests();
        _notifications.Info($"{guestName} wants to sing", $"{song.Artist} – {song.Title}");
        return new SongbookRequestResult(true, false, null);
    }).GetTask();

    public Task<bool> CancelAsync(string requestId, string clientId) => Dispatcher.UIThread.InvokeAsync(() =>
    {
        if (!_requests.Cancel(requestId, clientId))
        {
            return false;
        }

        RefreshRequests();
        return true;
    }).GetTask();

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
