using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Song sources + library. Sources are persisted as a settings document; songs live in the SQLite
/// repository, so the library is available immediately on the next start without rescanning.
/// </summary>
public sealed class LibraryService
{
    private const string SettingsName = "sources";

    private readonly ISettingsStore _settings;
    private readonly ISongRepository _repo;
    private readonly LocalFolderScanner _scanner;
    private readonly ILogger<LibraryService> _log;
    private SourcesDocument _doc;
    private IReadOnlyList<Song> _songs;

    public LibraryService(ISettingsStore settings, ISongRepository repo, LocalFolderScanner scanner, ILogger<LibraryService> log)
    {
        _settings = settings;
        _repo = repo;
        _scanner = scanner;
        _log = log;
        _doc = settings.Load(SettingsName, new SourcesDocument([]));
        _songs = repo.GetAll();
        _log.LogInformation("Library: {Songs} songs from {Sources} sources", _songs.Count, _doc.Sources.Count);
    }

    /// <summary>Raised on the calling thread after <see cref="Songs"/> or <see cref="Sources"/> changed.</summary>
    public event Action? Changed;

    public IReadOnlyList<SongSource> Sources => _doc.Sources;
    public IReadOnlyList<Song> Songs => _songs;

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
        _songs = _repo.GetAll();
        Changed?.Invoke();
    }

    public void RemoveSource(string sourceId)
    {
        _doc = _doc with { Sources = _doc.Sources.Where(s => s.Id != sourceId).ToList() };
        _settings.Save(SettingsName, _doc);
        _repo.RemoveSource(sourceId);
        _songs = _repo.GetAll();
        Changed?.Invoke();
    }

    public int CountFor(string sourceId) => _repo.CountBySource(sourceId);

    public sealed record SourcesDocument(IReadOnlyList<SongSource> Sources);
}
