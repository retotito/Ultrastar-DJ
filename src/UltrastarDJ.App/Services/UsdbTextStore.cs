using System.Globalization;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure;

namespace UltrastarDJ.App.Services;

/// <summary>
/// The USDB song texts on this computer: one readable file per song in a folder the DJ can choose
/// (<see cref="UsdbTextFiles"/>), and which of them are kept beyond the loaded ones (<see cref="UsdbTextsMode"/>).
/// A file's last-write time is when it was fetched — older than the song's last change on USDB means stale.
/// Thread-safe: the downloader writes from the background while a song loads.
/// </summary>
public sealed class UsdbTextStore
{
    private const string SettingsName = "usdb-texts";

    private readonly ISettingsStore _settings;
    private readonly IUsdbCatalog _catalog;
    private readonly ILogger<UsdbTextStore> _log;
    private readonly Lock _lock = new();
    private TextsDocument _doc;
    // id → file; built on first use (and after a folder change) by listing the folder.
    private Dictionary<int, string>? _files;
    // id → (artist, title) from the catalog, for the file names; refreshed after a sync.
    private Dictionary<int, (string Artist, string Title)>? _names;

    public UsdbTextStore(ISettingsStore settings, AppPaths paths, IUsdbCatalog catalog, ILogger<UsdbTextStore> log)
    {
        _settings = settings;
        _catalog = catalog;
        _log = log;
        DefaultFolder = Path.Combine(paths.Cache, "usdb");
        _doc = settings.Load(SettingsName, new TextsDocument(UsdbTextsMode.Loaded, null, false));
    }

    /// <summary>Mode, folder or pause changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>Where they are kept unless the DJ chose a folder: the app's cache.</summary>
    public string DefaultFolder { get; }
    public string Folder => _doc.Folder ?? DefaultFolder;
    public bool IsDefaultFolder => _doc.Folder is null;
    public UsdbTextsMode Mode => _doc.Mode;
    public bool Paused => _doc.Paused;

    public void SetMode(UsdbTextsMode mode) => Save(_doc with { Mode = mode });

    public void SetPaused(bool paused) => Save(_doc with { Paused = paused });

    private void Save(TextsDocument doc)
    {
        _doc = doc;
        _settings.Save(SettingsName, doc);
        Changed?.Invoke();
    }

    /// <summary>The catalog was synced: titles may have changed (new files get the new name).</summary>
    public void RefreshNames()
    {
        lock (_lock)
        {
            _names = null;
        }
    }

    /// <summary>The file of a song, or null when it was never fetched.</summary>
    public string? PathOf(int id)
    {
        lock (_lock)
        {
            return Files().GetValueOrDefault(id);
        }
    }

    /// <summary>Every song text on disk: id → when it was fetched (unix seconds).</summary>
    public IReadOnlyDictionary<int, long> OnDisk()
    {
        List<KeyValuePair<int, string>> files;
        lock (_lock)
        {
            files = [.. Files()];
        }

        Dictionary<int, long> written = new(files.Count);
        foreach ((int id, string path) in files)
        {
            written[id] = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
        }

        return written;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return Files().Count;
            }
        }
    }

    /// <summary>Stores a fetched text under its readable name (replacing a file of the same song with an older name).</summary>
    public async Task WriteAsync(int id, string txt, CancellationToken ct)
    {
        string folder = Folder;
        string name;
        string? old;
        lock (_lock)
        {
            name = Names().TryGetValue(id, out (string Artist, string Title) n)
                ? UsdbTextFiles.FileName(id, n.Artist, n.Title)
                : id.ToString(CultureInfo.InvariantCulture) + ".txt";
            old = Files().GetValueOrDefault(id);
        }

        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        // Written next to it and moved in: a half-written file must never look like a song.
        string temp = path + ".part";
        await File.WriteAllTextAsync(temp, txt, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        if (old is not null && !string.Equals(old, path, StringComparison.Ordinal))
        {
            File.Delete(old);
        }

        lock (_lock)
        {
            Files()[id] = path;
        }
    }

    /// <summary>
    /// Moves every song text to <paramref name="folder"/> (keeping when each was fetched) and keeps them there from
    /// now on. Null = back to the default folder. Returns how many were moved.
    /// </summary>
    public int MoveTo(string? folder)
    {
        string target = folder ?? DefaultFolder;
        int moved = 0;
        lock (_lock)
        {
            Directory.CreateDirectory(target);
            foreach ((int id, string path) in Files().ToList())
            {
                string to = Path.Combine(target, Path.GetFileName(path));
                if (!string.Equals(path, to, StringComparison.Ordinal))
                {
                    File.Move(path, to, overwrite: true);
                    moved++;
                }

                _files![id] = to;
            }

            _doc = _doc with { Folder = folder };
        }

        _settings.Save(SettingsName, _doc);
        _log.LogInformation("USDB song texts moved to {Folder} ({Count} files)", target, moved);
        Changed?.Invoke();
        return moved;
    }

    // Under _lock. The folder is listed once; files of the old form ("23775.txt") get their readable name on the way.
    private Dictionary<int, string> Files()
    {
        if (_files is not null)
        {
            return _files;
        }

        _files = [];
        if (!Directory.Exists(Folder))
        {
            return _files;
        }

        int renamed = 0;
        foreach (string path in Directory.EnumerateFiles(Folder, "*.txt"))
        {
            string file = Path.GetFileName(path);
            if (UsdbTextFiles.IdOf(file) is not { } id)
            {
                continue;
            }

            string kept = path;
            if (file == id.ToString(CultureInfo.InvariantCulture) + ".txt" && Names().TryGetValue(id, out (string Artist, string Title) n))
            {
                kept = Path.Combine(Folder, UsdbTextFiles.FileName(id, n.Artist, n.Title));
                try
                {
                    File.Move(path, kept, overwrite: true);
                    renamed++;
                }
                catch (IOException ex)
                {
                    _log.LogWarning("Could not rename {File}: {Error}", file, ex.Message);
                    kept = path;
                }
            }

            _files[id] = kept;
        }

        if (renamed > 0)
        {
            _log.LogInformation("USDB song texts: {Count} files renamed to \"Artist - Title [id].txt\"", renamed);
        }

        return _files;
    }

    // Under _lock.
    private Dictionary<int, (string Artist, string Title)> Names()
        => _names ??= _catalog.GetAll().ToDictionary(e => e.SongId, e => (e.Artist, e.Title));

    /// <param name="Mode">Which texts are kept beyond the loaded ones.</param>
    /// <param name="Folder">Null = <see cref="DefaultFolder"/>.</param>
    /// <param name="Paused">The DJ paused the background download.</param>
    public sealed record TextsDocument(UsdbTextsMode Mode, string? Folder, bool Paused);
}
