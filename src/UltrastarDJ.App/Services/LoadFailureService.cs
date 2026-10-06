using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Remembers songs that could not be loaded for a reason of their own (<see cref="LoadFailure"/>) so the library can
/// mark them. A mark goes away when the song loads again (Preview or Game Player) or when it changed since — fixed on
/// USDB or edited locally (fingerprint). UI thread.
/// </summary>
public sealed class LoadFailureService
{
    private readonly ILoadFailureStore _store;
    private readonly LibraryService _library;
    private readonly ILogger<LoadFailureService> _log;
    private readonly Dictionary<string, LoadFailure> _failures;

    public LoadFailureService(ILoadFailureStore store, LibraryService library, ILogger<LoadFailureService> log)
    {
        _store = store;
        _library = library;
        _log = log;
        _failures = store.All().ToDictionary(f => f.SongId);
    }

    /// <summary>A mark was added or removed: the library redraws its rows.</summary>
    public event Action? Changed;

    /// <summary>The mark for this version of the song, or null. A mark for an older version is dropped here.</summary>
    public LoadFailure? For(Song song)
    {
        if (!_failures.TryGetValue(song.Id, out LoadFailure? f))
        {
            return null;
        }

        if (f.AppliesTo(song, LastWriteUtc))
        {
            return f;
        }

        _log.LogInformation("{Song}: changed since it failed to load — mark removed", song.Id);
        _failures.Remove(song.Id);
        _store.Remove(song.Id);
        return null;
    }

    /// <param name="song">The song as the library knows it (its id and version).</param>
    /// <param name="reason">What the DJ was told.</param>
    public void Failed(Song song, string reason)
    {
        // A drive pulled mid-load fails like a broken song; the song is fine — no mark.
        if (song.UsdbId is null && !_library.IsReachableNow(song.SourceId))
        {
            return;
        }

        LoadFailure f = new(song.Id, reason, DateTime.UtcNow, LoadFailure.FingerprintOf(song, LastWriteUtc));
        _failures[song.Id] = f;
        _store.Save(f);
        _log.LogInformation("{Song}: marked as not loadable — {Reason}", song.Id, reason);
        Changed?.Invoke();
    }

    public void Loaded(Song song)
    {
        if (_failures.Remove(song.Id))
        {
            _store.Remove(song.Id);
            _log.LogInformation("{Song}: loaded again — mark removed", song.Id);
            Changed?.Invoke();
        }
    }

    private static DateTime? LastWriteUtc(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
}
