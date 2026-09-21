using System.Globalization;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure;

namespace UltrastarDJ.App.Services;

/// <summary>
/// USDB account, catalog sync and on-demand song text. Credentials are persisted so the app reconnects at
/// startup; the catalog lives in SQLite and is merged into the library by <see cref="LibraryService"/>.
/// Song texts are cached on disk (one file per id) so a song plays offline once it has been loaded.
/// </summary>
public sealed class UsdbService : IDisposable
{
    private const string SettingsName = "usdb";

    private readonly IUsdbClient _client;
    private readonly IUsdbCatalog _catalog;
    private readonly LibraryService _library;
    private readonly ISettingsStore _settings;
    private readonly string _txtCacheDir;
    private readonly ILogger<UsdbService> _log;
    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private UsdbDocument _doc;
    private CancellationTokenSource? _syncCts;

    public UsdbService(IUsdbClient client, IUsdbCatalog catalog, LibraryService library, ISettingsStore settings, AppPaths paths, ILogger<UsdbService> log)
    {
        _client = client;
        _catalog = catalog;
        _library = library;
        _settings = settings;
        _log = log;
        _doc = settings.Load(SettingsName, new UsdbDocument(null, null, false));
        _txtCacheDir = Path.Combine(paths.Cache, "usdb");
        Directory.CreateDirectory(_txtCacheDir);
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

    /// <summary>Startup: reconnect with saved credentials and pull changes. Never throws.</summary>
    public async Task AutoConnectAsync()
    {
        if (!HasCredentials)
        {
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

    /// <summary>Song text for a USDB song: disk cache first, else fetched (logging in with saved credentials if needed).</summary>
    public async Task<string> GetSongTxtAsync(int songId, CancellationToken ct = default)
    {
        string path = Path.Combine(_txtCacheDir, songId.ToString(CultureInfo.InvariantCulture) + ".txt");
        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }

        await EnsureLoggedInAsync(ct).ConfigureAwait(false);
        string txt = await _client.GetSongTxtAsync(songId, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(path, txt, ct).ConfigureAwait(false);
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
