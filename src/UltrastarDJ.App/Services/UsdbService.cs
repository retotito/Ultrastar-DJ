using System.Globalization;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure;

namespace UltrastarDJ.App.Services;

/// <summary>
/// USDB account, catalog sync and on-demand song text. Credentials are persisted so the app reconnects at
/// startup; the catalog lives in SQLite and is merged into the library by <see cref="LibraryService"/>.
/// Song texts are kept on disk (<see cref="UsdbTextStore"/>) so a song plays offline once it has been loaded.
/// </summary>
public sealed class UsdbService : IDisposable
{
    private const string SettingsName = "usdb";

    private readonly IUsdbClient _client;
    private readonly IUsdbCatalog _catalog;
    private readonly LibraryService _library;
    private readonly ISettingsStore _settings;
    private readonly UsdbTextStore _texts;
    private readonly NotificationService _notifications;
    private readonly ConnectivityService _connectivity;
    private readonly ILogger<UsdbService> _log;
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private UsdbDocument _doc;
    private CancellationTokenSource? _syncCts;

    public UsdbService(IUsdbClient client, IUsdbCatalog catalog, LibraryService library, ISettingsStore settings, UsdbTextStore texts,
        NotificationService notifications, ConnectivityService connectivity, ILogger<UsdbService> log)
    {
        _notifications = notifications;
        _connectivity = connectivity;
        connectivity.Changed += OnConnectivityChanged;
        _client = client;
        _catalog = catalog;
        _library = library;
        _settings = settings;
        _log = log;
        _doc = settings.Load(SettingsName, new UsdbDocument(null, null, false));
        _texts = texts;
        CatalogCount = catalog.Count;
    }

    /// <summary>Raised on whichever thread changed the state; view models marshal.</summary>
    public event Action? Changed;

    public string? Username => _doc.Username;
    public bool HasCredentials => !string.IsNullOrEmpty(_doc.Username) && !string.IsNullOrEmpty(_doc.Password);
    public bool IsConnected => _client.IsLoggedIn;
    public bool IsSyncing => _syncCts is not null;
    public int SyncFetched { get; private set; }
    public int CatalogCount { get; private set; }
    public string Status { get; private set; } = "";

    /// <summary>Offline: grey USDB songs out. Back online: un-grey (session still valid) or log in again.</summary>
    private void OnConnectivityChanged(bool online)
    {
        if (!HasCredentials)
        {
            return;
        }

        if (!online)
        {
            _library.SetUsdbOnline(false);
            SetStatus("Offline — no internet connection");
        }
        else if (IsConnected)
        {
            _library.SetUsdbOnline(true);
            SetStatus($"Connected as {_doc.Username}");
        }
        else
        {
            _ = AutoConnectAsync();
        }
    }

    /// <summary>Startup and back-online: reconnect with saved credentials and pull changes. Never throws.</summary>
    public async Task AutoConnectAsync()
    {
        if (!HasCredentials)
        {
            return;
        }

        if (_connectivity.IsOnline is false)
        {
            // The connectivity toast already says so; reconnect happens when it comes back.
            _library.SetUsdbOnline(false);
            SetStatus("Offline — no internet connection");
            return;
        }

        try
        {
            await ConnectAsync(_doc.Username!, _doc.Password!).ConfigureAwait(false);
        }
        catch (UsdbException ex)
        {
            _log.LogWarning("USDB auto-connect failed: {Error}", ex.Message);
            SetStatus($"Offline — {ex.Message}");
            // Internet works but USDB does not (no internet is reported by ConnectivityService).
            if (_connectivity.IsOnline is true)
            {
                _notifications.Warn("USDB is offline", "USDB songs are greyed out until it connects. " + ex.Message);
            }
        }
    }

    /// <summary>Logs in, stores the credentials and syncs (full when the catalog is incomplete). Throws <see cref="UsdbException"/> on network failure.</summary>
    public async Task<bool> ConnectAsync(string username, string password, CancellationToken ct = default)
    {
        SetStatus("Connecting…");
        bool ok = await _client.LoginAsync(username, password, ct).ConfigureAwait(false);
        if (!ok)
        {
            SetStatus("Login rejected — check username and password");
            return false;
        }

        _doc = _doc with { Username = username, Password = password };
        _settings.Save(SettingsName, _doc);
        _library.SetUsdbOnline(true);
        SetStatus($"Connected as {username}");
        await SyncAsync(full: !_doc.FullSyncDone).ConfigureAwait(false);
        return true;
    }

    /// <summary>Full: every page, upserted as it arrives (abort keeps what was fetched). Incremental: changes since the watermark.</summary>
    public async Task SyncAsync(bool full)
    {
        if (IsSyncing || !IsConnected)
        {
            return;
        }

        CancellationTokenSource cts = new();
        _syncCts = cts;
        SyncFetched = 0;
        SetStatus(full ? "Downloading catalog…" : "Checking for changes…");
        try
        {
            if (full)
            {
                await foreach (IReadOnlyList<UsdbCatalogEntry> page in _client.FetchAllPagesAsync(cts.Token).ConfigureAwait(false))
                {
                    _catalog.Upsert(page);
                    SyncFetched += page.Count;
                    SetStatus($"Downloading catalog… {SyncFetched:N0} songs");
                }

                _doc = _doc with { FullSyncDone = true };
                _settings.Save(SettingsName, _doc);
            }
            else
            {
                (long last, IReadOnlySet<int> ids) = _catalog.Watermark();
                IReadOnlyList<UsdbCatalogEntry> updated = await _client.FetchUpdatedAsync(last, ids, null, cts.Token).ConfigureAwait(false);
                _catalog.Upsert(updated);
                SyncFetched = updated.Count;
            }

            CatalogCount = _catalog.Count;
            SetStatus($"{CatalogCount:N0} USDB songs" + (full ? "" : $" ({SyncFetched:N0} updated)"));
        }
        catch (OperationCanceledException)
        {
            CatalogCount = _catalog.Count;
            SetStatus($"Sync stopped — {CatalogCount:N0} songs so far" + (full ? ", resume with Sync" : ""));
        }
        catch (UsdbException ex)
        {
            _log.LogWarning("USDB sync failed: {Error}", ex.Message);
            CatalogCount = _catalog.Count;
            SetStatus($"Sync failed — {ex.Message}");
        }
        finally
        {
            _syncCts = null;
            cts.Dispose();
            _texts.RefreshNames();
            _library.RefreshUsdb();
            Changed?.Invoke();
        }
    }

    public void AbortSync() => _syncCts?.Cancel();

    /// <summary>Forgets the account and removes the USDB songs from the library. Cached song texts stay.</summary>
    public void Disconnect()
    {
        AbortSync();
        _client.Logout();
        _doc = new UsdbDocument(null, null, false);
        _settings.Save(SettingsName, _doc);
        _catalog.Clear();
        CatalogCount = 0;
        _library.SetUsdbOnline(false);
        _library.RefreshUsdb();
        SetStatus("Disconnected");
    }

    /// <summary>
    /// Song text for a USDB song: disk cache first, else fetched (logging in with saved credentials if needed).
    /// A cached text older than the song's last change on USDB (<paramref name="usdbMtime"/>, from the catalog) is
    /// fetched again — otherwise a song fixed on USDB would keep playing its old text. If that refetch fails
    /// (offline), the cached text is still used.
    /// </summary>
    public async Task<string> GetSongTxtAsync(int songId, long? usdbMtime = null, CancellationToken ct = default)
    {
        string? path = _texts.PathOf(songId);
        bool cached = path is not null && File.Exists(path);
        bool stale = cached && usdbMtime is > 0 && File.GetLastWriteTimeUtc(path!) < DateTimeOffset.FromUnixTimeSeconds(usdbMtime.Value).UtcDateTime;
        if (cached && !stale)
        {
            return await File.ReadAllTextAsync(path!, ct).ConfigureAwait(false);
        }

        try
        {
            string txt = await DownloadTxtAsync(songId, ct).ConfigureAwait(false);
            if (stale)
            {
                _log.LogInformation("USDB song {Id} changed on USDB — song text refreshed", songId);
            }

            return txt;
        }
        catch (Exception ex) when (stale && ex is UsdbException or HttpRequestException)
        {
            _log.LogWarning("USDB song {Id} changed on USDB but could not be refreshed ({Error}) — using the cached text", songId, ex.Message);
            return await File.ReadAllTextAsync(path!, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Fetches a song text from USDB and keeps it on disk (logging in with saved credentials if needed).</summary>
    public async Task<string> DownloadTxtAsync(int songId, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct).ConfigureAwait(false);
        string txt = await _client.GetSongTxtAsync(songId, ct).ConfigureAwait(false);
        await _texts.WriteAsync(songId, txt, ct).ConfigureAwait(false);
        return txt;
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (IsConnected)
        {
            return;
        }

        if (!HasCredentials)
        {
            throw new UsdbException("Not connected to USDB — connect under Song Sources");
        }

        await _loginGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsConnected && !await _client.LoginAsync(_doc.Username!, _doc.Password!, ct).ConfigureAwait(false))
            {
                throw new UsdbException("USDB login rejected — check the credentials under Song Sources");
            }

            _library.SetUsdbOnline(true);
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _syncCts?.Cancel();
        _loginGate.Dispose();
    }

    public sealed record UsdbDocument(string? Username, string? Password, bool FullSyncDone);
}
