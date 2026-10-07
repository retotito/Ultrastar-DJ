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
using UltrastarDJ.Infrastructure;
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
    private readonly SidecarLocator _sidecars;
    private readonly PlaybackService _playback;
    private readonly Playlist _playlist;
    private readonly NotificationService _notifications;
    private readonly ISettingsStore _settings;
    private readonly ILogger<SongbookService> _log;
    private readonly SongbookServer _server;
    private SongbookDocument _doc;

    public SongbookService(LibraryService library, PlaybackService playback, Playlist playlist, NotificationService notifications,
        ISettingsStore settings, UsdbService usdb, SidecarLocator sidecars, ILoggerFactory loggers)
    {
        _sidecars = sidecars;
        _library = library;
        _usdb = usdb;
        library.Changed += OnLibraryChanged;
        library.AvailabilityChanged += OnLibraryChanged;
        _playback = playback;
        _playlist = playlist;
        // Remember which accepted requests went on stage (their status turns "sung" once another song is loaded —
        // from the queue or the library), and refresh who is shown as requester in the queue and the Game Player.
        playlist.Changed += OnStageChanged;
        playback.StateChanged += _ => OnStageChanged();
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
    /// <summary>Guests may request songs; off = the songbook is only for browsing (not every DJ wants requests).</summary>
    public bool RequestsOpen => !_doc.RequestsClosed;

    public void SetRequestsOpen(bool open) => Save(_doc with { RequestsClosed = !open });
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

        // Public-link mode: only the public address works (none while connecting); Wi-Fi mode: the Wi-Fi address.
        if (_doc.PublicLink)
        {
            return PublicUrl;
        }

        return SongbookAddress.Best(addresses) is { } best ? $"http://{best}:{_doc.Port}" : null;
    }

    public async Task StartAsync()
    {
        LastError = null;
        try
        {
            // One way in at a time: in public-link mode only cloudflared (on this Mac) reaches the server.
            await _server.StartAsync(_doc.Port, localOnly: _doc.PublicLink).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
        {
            LastError = $"Cannot listen on port {_doc.Port}: {ex.Message}";
            _log.LogWarning(ex, "Songbook start failed");
        }

        Notify();
        if (_server.IsRunning && _doc.PublicLink)
        {
            await StartTunnelAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        await StopTunnelAsync().ConfigureAwait(false);
        await _server.StopAsync().ConfigureAwait(false);
        Notify();
    }

    // ── Public link (Cloudflare Quick Tunnel) for guests on mobile data ──
    private const int TunnelRetries = 3;
    private static readonly TimeSpan TunnelRetryDelay = TimeSpan.FromSeconds(5);
    private CloudflareTunnel? _tunnel;
    private int _tunnelRetries;

    public bool PublicLink => _doc.PublicLink;
    /// <summary>The public https address while the tunnel runs; the beamer's QR code shows it instead of the Wi-Fi one.</summary>
    public string? PublicUrl { get; private set; }
    /// <summary>"Connecting…", "Reconnecting…" or why the link failed; empty while it works or is off.</summary>
    public string PublicStatus { get; private set; } = "";

    /// <summary>
    /// Wi-Fi or public link — one at a time. While running, the active one stops and the other starts: the server
    /// restarts on the other binding (all interfaces / this Mac only) and the tunnel follows.
    /// </summary>
    public async Task SetPublicLinkAsync(bool on)
    {
        if (on == _doc.PublicLink)
        {
            return;
        }

        bool wasRunning = IsRunning;
        if (wasRunning)
        {
            await StopAsync().ConfigureAwait(false);
        }

        Save(_doc with { PublicLink = on });
        _tunnelRetries = 0;
        if (wasRunning)
        {
            await StartAsync().ConfigureAwait(false);
        }

        Notify();
    }

    private async Task StartTunnelAsync()
    {
        if (_sidecars.Cloudflared is not { } exe)
        {
            SetPublic(null, "cloudflared is missing — run scripts/fetch-natives");
            return;
        }

        // Anyone with the link could open it: the party PIN keeps strangers out.
        if (!_doc.PinEnabled)
        {
            SetPinEnabled(true);
        }

        SetPublic(null, _tunnelRetries == 0 ? "Connecting…" : "Reconnecting…");
        _tunnel ??= new CloudflareTunnel(exe, _log);
        _tunnel.Ended -= OnTunnelEnded;
        _tunnel.Ended += OnTunnelEnded;
        try
        {
            string url = await _tunnel.StartAsync(_doc.Port).ConfigureAwait(false);
            _tunnelRetries = 0;
            SetPublic(url, "");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.LogWarning(ex, "Public link failed");
            SetPublic(null, ex.Message);
        }
    }

    // cloudflared died (network gone, Cloudflare hiccup): a few retries, then the reason in the panel. The beamer's QR
    // code is hidden meanwhile (the Wi-Fi address is closed in this mode).
    private void OnTunnelEnded()
    {
        if (!_doc.PublicLink || !IsRunning)
        {
            return;
        }

        if (_tunnelRetries++ >= TunnelRetries)
        {
            SetPublic(null, "The public link was lost — stop and start the songbook, or switch to Wi-Fi.");
            _notifications.Warn("Public songbook link lost", "Guests cannot reach the songbook — switch to Wi-Fi in the Songbook panel, or restart it.");
            return;
        }

        SetPublic(null, "Reconnecting…");
        _ = Task.Delay(TunnelRetryDelay).ContinueWith(_ => StartTunnelAsync(), TaskScheduler.Default).Unwrap();
    }

    private async Task StopTunnelAsync()
    {
        if (_tunnel is { } t)
        {
            t.Ended -= OnTunnelEnded;
            await t.StopAsync().ConfigureAwait(false);
        }

        SetPublic(null, "");
    }

    private void SetPublic(string? url, string status)
    {
        PublicUrl = url;
        PublicStatus = status;
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
    public string? RequesterOf(string songId) => _requests.RequesterOf(songId, QueueIds(), _playlist.ActiveIndex, LoadedId);

    private IReadOnlyList<string> QueueIds() => [.. _playlist.Items.Select(s => s.Id)];

    /// <summary>The song in the Game Player — the one on stage, wherever it was loaded from.</summary>
    private string? LoadedId => _playback.Song?.Id;

    private void OnStageChanged()
    {
        _requests.Observe(LoadedId);
        Changed?.Invoke();
    }

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

        for (int i = active + 1; i < ids.Count; i++)
        {
            taken[ids[i]] = i == active + 1 ? "Up next" : $"In the queue #{i - active}";
        }

        if (LoadedId is { } loaded)
        {
            taken[loaded] = "On stage";
        }

        List<SongbookMine> mine = [];
        if (clientId is not null)
        {
            foreach (GuestRequest r in _requests.Of(clientId).Reverse())
            {
                GuestRequestStatus status = GuestRequests.StatusOf(r, ids, active, LoadedId, out int position);
                Song? song = _library.Songs.FirstOrDefault(s => s.Id == r.SongId);
                mine.Add(new SongbookMine(r.Id, r.SongId, song?.Title ?? "", song?.Artist ?? "", StatusText(status, position), status == GuestRequestStatus.Waiting));
            }
        }

        return new SongbookState(now is null ? null : ToGuestSong(now), queue, _library.Songs.Count, LibraryVersion, mine, taken, RequestsOpen);
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

        if (!RequestsOpen)
        {
            return new SongbookRequestResult(false, false, "The DJ isn't taking requests right now.");
        }

        RequestOutcome outcome = _requests.Add(songId, guestName, clientId, DateTime.Now, QueueIds(), _playlist.ActiveIndex, LoadedId);
        if (outcome.Request is null)
        {
            return new SongbookRequestResult(false, false, outcome.Refusal);
        }

        RefreshRequests();
        _notifications.Request($"{guestName} wants to sing", $"{song.Artist} – {song.Title}");
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

    public async ValueTask DisposeAsync()
    {
        await StopTunnelAsync().ConfigureAwait(false);
        await _server.DisposeAsync().ConfigureAwait(false);
    }

    public sealed record SongbookDocument(int Port, bool PinEnabled, string Pin, bool AutoStart)
    {
        /// <summary>Stored inverted so settings files from before the switch keep requests open.</summary>
        public bool RequestsClosed { get; init; }
        /// <summary>Guests connect via the public link instead of the Wi-Fi (default Wi-Fi: no internet needed).</summary>
        public bool PublicLink { get; init; }

        public static SongbookDocument Default() => new(4747, false, NewPin(), false);
    }
}
