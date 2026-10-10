using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Song sources + library. Sources are persisted as a settings document; songs live in the SQLite
/// repository, so the library is available immediately on the next start without rescanning.
/// </summary>
public sealed class LibraryService : IDisposable
{
    private const string SettingsName = "sources";

    private readonly ISettingsStore _settings;
    private readonly ISongRepository _repo;
    private readonly IUsdbCatalog _usdb;
    private readonly LocalFolderScanner _scanner;
    private readonly NotificationService _notifications;
    private readonly ILogger<LibraryService> _log;
    private int _checking;
    private SourcesDocument _doc;
    private IReadOnlyList<Song> _localSongs;
    private List<Song> _usdbSongs;
    private IReadOnlyList<Song> _songs;
    private HashSet<string> _unavailable = [];
    private bool _usdbOnline;
    private readonly Timer _availabilityTimer;

    public LibraryService(ISettingsStore settings, ISongRepository repo, IUsdbCatalog usdb, LocalFolderScanner scanner, NotificationService notifications,
        ILogger<LibraryService> log)
    {
        _settings = settings;
        _repo = repo;
        _usdb = usdb;
        _scanner = scanner;
        _notifications = notifications;
        _log = log;
        _doc = settings.Load(SettingsName, new SourcesDocument([]));
        _localSongs = repo.GetAll();
        _usdbSongs = usdb.GetAll().Select(e => e.ToSong()).ToList();
        _songs = Merge();
        _log.LogInformation("Library: {Songs} songs from {Sources} sources + {Usdb} USDB", _localSongs.Count, _doc.Sources.Count, _usdbSongs.Count);
        CheckAvailability(atStart: true);
        // Folders on USB drives come and go; poll cheaply (Directory.Exists) instead of a watcher per source.
        _availabilityTimer = new Timer(_ => CheckAvailability(atStart: false), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        if (repo.NeedsRescan)
        {
            _ = RescanAfterUpgradeAsync();
        }
    }

    // The library was upgraded (duets): rescan the connected folders once in the background. Unplugged ones keep their
    // songs as they were and get the rescan when the drive comes back (RescanReturnedAsync).
    private async Task RescanAfterUpgradeAsync()
    {
        foreach (SongSource source in _doc.Sources.Where(s => s.Enabled && IsReachableNow(s.Id)).ToList())
        {
            try
            {
                await RescanAsync(source.Id).ConfigureAwait(false);
                _log.LogInformation("Library upgrade: rescanned {Source} (duets)", source.Label);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "Library upgrade: rescan of {Source} failed", source.Label);
            }
        }
    }

    /// <summary>Raised on the calling thread after <see cref="Songs"/> or <see cref="Sources"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on a timer thread when a source folder appeared or disappeared.</summary>
    public event Action? AvailabilityChanged;

    public IReadOnlyList<SongSource> Sources => _doc.Sources;
    public IReadOnlyList<Song> Songs => _songs;
    /// <summary>USDB songs in the library — 0 while USDB is switched off.</summary>
    public int UsdbCount => _doc.UsdbHidden ? 0 : _usdbSongs.Count;
    public bool UsdbEnabled => !_doc.UsdbHidden;

    /// <summary>
    /// Song Sources → USDB switch: off leaves the USDB songs out of the library, the source filter and the songbook
    /// without logging out (sync keeps working).
    /// </summary>
    public void SetUsdbEnabled(bool enabled)
    {
        _doc = _doc with { UsdbHidden = !enabled };
        _settings.Save(SettingsName, _doc);
        _songs = Merge();
        Changed?.Invoke();
    }

    public bool IsAvailable(string sourceId) => sourceId == UsdbCatalogEntry.SourceId ? _usdbOnline : !_unavailable.Contains(sourceId);
    /// <summary>Checks the folder right now (the poll runs every 5 s; a drive pulled a second ago must count too).</summary>
    public bool IsReachableNow(string sourceId)
        => sourceId == UsdbCatalogEntry.SourceId || _doc.Sources.FirstOrDefault(s => s.Id == sourceId)?.Path is not { } p || Directory.Exists(p);

    public string SourceLabel(string sourceId) => sourceId == UsdbCatalogEntry.SourceId ? "USDB" : _doc.Sources.FirstOrDefault(s => s.Id == sourceId)?.Label ?? sourceId;

    /// <summary>USDB songs need a live session to fetch their txt; offline/disconnected greys them like an unplugged drive.</summary>
    public void SetUsdbOnline(bool online)
    {
        if (_usdbOnline != online)
        {
            _usdbOnline = online;
            AvailabilityChanged?.Invoke();
        }
    }

    /// <summary>Re-reads the USDB catalog after a sync or disconnect.</summary>
    public void RefreshUsdb()
    {
        _usdbSongs = _usdb.GetAll().Select(e => e.ToSong()).ToList();
        _songs = Merge();
        Changed?.Invoke();
    }

    // Switched-off sources (Song Sources toggle) are left out of the library, as in the prototype.
    private IReadOnlyList<Song> Merge()
    {
        HashSet<string> off = [.. _doc.Sources.Where(s => !s.Enabled).Select(s => s.Id)];
        IReadOnlyList<Song> local = off.Count == 0 ? _localSongs : [.. _localSongs.Where(s => !off.Contains(s.SourceId))];
        return _usdbSongs.Count == 0 || _doc.UsdbHidden ? local : [.. local, .. _usdbSongs];
    }

    /// <summary>
    /// Which enabled folders are reachable. A drive that went away: toast, its songs greyed. One that came back:
    /// rescanned (songs added or fixed elsewhere meanwhile), then a toast with the count. At start, missing folders
    /// get a toast too, so the DJ knows before the party.
    /// </summary>
    private void CheckAvailability(bool atStart, bool quiet = false)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1)
        {
            return;
        }

        try
        {
            HashSet<string> gone = _doc.Sources.Where(s => s.Enabled && s.Path is { } p && !Directory.Exists(p)).Select(s => s.Id).ToHashSet();
            if (gone.SetEquals(_unavailable))
            {
                return;
            }

            (IReadOnlyList<string> wentAway, IReadOnlyList<string> cameBack) = SourceAvailability.Diff(_unavailable, gone);
            _unavailable = gone;
            _log.LogInformation("Source availability: {Away} gone, {Back} back, {Unavailable} unavailable", wentAway.Count, cameBack.Count, gone.Count);
            AvailabilityChanged?.Invoke();
            if (quiet)
            {
                return;
            }

            foreach (string id in wentAway)
            {
                string label = SourceLabel(id);
                int count = CountFor(id);
                if (atStart)
                {
                    _notifications.Warn($"Not connected: {label}", $"Its {count} songs are greyed out until the drive is connected.");
                }
                else
                {
                    _notifications.Warn($"Drive removed: {label}", $"Its {count} songs are greyed out until it is back.");
                }
            }

            foreach (string id in cameBack)
            {
                _ = RescanReturnedAsync(id);
            }
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    private async Task RescanReturnedAsync(string sourceId)
    {
        string label = SourceLabel(sourceId);
        try
        {
            await RescanAsync(sourceId).ConfigureAwait(false);
            _notifications.Success($"Drive connected: {label}", $"{CountFor(sourceId)} songs available again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Rescan of returned source {Source} failed", label);
            _notifications.Success($"Drive connected: {label}", "Its songs are available again (the rescan failed — use Rescan in Song Sources).");
        }
    }

    /// <summary>Song Sources toggle: a switched-off folder's songs leave the library (and the source filter) until switched on.</summary>
    public void SetEnabled(string sourceId, bool enabled)
    {
        _doc = _doc with { Sources = [.. _doc.Sources.Select(s => s.Id == sourceId ? s with { Enabled = enabled } : s)] };
        _settings.Save(SettingsName, _doc);
        _songs = Merge();
        CheckAvailability(atStart: false, quiet: true);
        Changed?.Invoke();
    }

    public async Task AddLocalFolderAsync(string path, IProgress<LocalFolderScanner.Progress>? progress = null, CancellationToken ct = default)
    {
        if (_doc.Sources.Any(s => s.Path is { } p && string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.Ordinal)))
        {
            _log.LogInformation("Source already present: {Path}", path);
            return;
        }

        SongSource source = SongSource.LocalFolder(path);
        _doc = _doc with { Sources = [.. _doc.Sources, source] };
        _settings.Save(SettingsName, _doc);
        Changed?.Invoke();
        await RescanAsync(source.Id, progress, ct).ConfigureAwait(false);
    }

    public async Task RescanAsync(string sourceId, IProgress<LocalFolderScanner.Progress>? progress = null, CancellationToken ct = default)
    {
        SongSource? source = _doc.Sources.FirstOrDefault(s => s.Id == sourceId);
        if (source?.Path is null)
        {
            return;
        }

        IReadOnlyList<Song> songs = await _scanner.ScanAsync(source.Id, source.Path, progress, ct).ConfigureAwait(false);
        _repo.ReplaceSource(source.Id, songs);
        _localSongs = _repo.GetAll();
        _songs = Merge();
        Changed?.Invoke();
    }

    public void RemoveSource(string sourceId)
    {
        _doc = _doc with { Sources = _doc.Sources.Where(s => s.Id != sourceId).ToList() };
        _settings.Save(SettingsName, _doc);
        _repo.RemoveSource(sourceId);
        _localSongs = _repo.GetAll();
        _songs = Merge();
        Changed?.Invoke();
    }

    public int CountFor(string sourceId) => _repo.CountBySource(sourceId);

    public void Dispose() => _availabilityTimer.Dispose();

    public sealed record SourcesDocument(IReadOnlyList<SongSource> Sources)
    {
        /// <summary>USDB switched off in Song Sources (stored inverted: older files keep USDB on).</summary>
        public bool UsdbHidden { get; init; }
    }
}
