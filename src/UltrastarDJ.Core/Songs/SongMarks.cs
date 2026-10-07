namespace UltrastarDJ.Core.Songs;

/// <summary>
/// What the DJ marked on a song. <see cref="Artist"/> / <see cref="Title"/> are kept so a local song's mark finds it
/// again after its folder moved (a local song's id contains its path).
/// </summary>
public sealed record SongMark(string SongId, string Artist, string Title, bool Favourite = false, bool Broken = false, string? Note = null)
{
    /// <summary>Nothing left worth keeping.</summary>
    public bool IsEmpty => !Favourite && !Broken && string.IsNullOrWhiteSpace(Note);
}

/// <summary>All marks, by song id. Not thread-safe: the app uses it on the UI thread.</summary>
public sealed class SongMarks
{
    private readonly Dictionary<string, SongMark> _byId;

    public SongMarks(IEnumerable<SongMark> marks)
    {
        _byId = marks.Where(m => !m.IsEmpty).GroupBy(m => m.SongId).ToDictionary(g => g.Key, g => g.Last());
        Rebuild();
    }

    public IReadOnlyCollection<SongMark> All => _byId.Values;
    public IReadOnlySet<string> FavouriteIds { get; private set; } = new HashSet<string>();
    public IReadOnlySet<string> BrokenIds { get; private set; } = new HashSet<string>();

    public SongMark? For(Song song) => _byId.GetValueOrDefault(song.Id);

    /// <summary>Changes this song's mark (created if missing); an empty mark is removed. Returns the mark, or null.</summary>
    public SongMark? Set(Song song, Func<SongMark, SongMark> change)
    {
        SongMark current = For(song) ?? new SongMark(song.Id, song.Artist, song.Title);
        SongMark next = change(current) with { SongId = song.Id, Artist = song.Artist, Title = song.Title };
        if (next.IsEmpty)
        {
            _byId.Remove(song.Id);
        }
        else
        {
            _byId[song.Id] = next;
        }

        Rebuild();
        return next.IsEmpty ? null : next;
    }

    /// <summary>
    /// Local marks whose song is gone move to a song of the same source with the same artist and title that has no mark
    /// yet (the folder inside the source was moved or renamed). Never across sources: a source switched off takes its
    /// songs out of the library, and its marks must wait for them, not jump to a copy elsewhere. USDB marks never move:
    /// their ids are stable. Returns whether anything moved.
    /// </summary>
    public bool Reattach(IEnumerable<Song> library)
    {
        List<Song> songs = library as List<Song> ?? library.ToList();
        HashSet<string> present = songs.Select(s => s.Id).ToHashSet();
        List<SongMark> orphans = _byId.Values.Where(m => !present.Contains(m.SongId) && !m.SongId.StartsWith(UsdbCatalogEntry.SourceId + "::", StringComparison.Ordinal)).ToList();
        if (orphans.Count == 0)
        {
            return false;
        }

        ILookup<string, Song> byKey = songs.Where(s => s.UsdbId is null && !_byId.ContainsKey(s.Id)).ToLookup(s => Key(s.SourceId, s.Artist, s.Title));
        bool moved = false;
        foreach (SongMark m in orphans)
        {
            if (byKey[Key(SourceOf(m.SongId), m.Artist, m.Title)].FirstOrDefault(s => !_byId.ContainsKey(s.Id)) is { } song)
            {
                _byId.Remove(m.SongId);
                _byId[song.Id] = m with { SongId = song.Id };
                moved = true;
            }
        }

        if (moved)
        {
            Rebuild();
        }

        return moved;
    }

    private static string Key(string sourceId, string artist, string title)
        => $"{sourceId}\u0001{artist.Trim().ToLowerInvariant()}\u0001{title.Trim().ToLowerInvariant()}";

    // Song ids are "{SourceId}::{…}".
    private static string SourceOf(string songId) => songId.IndexOf("::", StringComparison.Ordinal) is var i and > 0 ? songId[..i] : "";

    private void Rebuild()
    {
        FavouriteIds = _byId.Values.Where(m => m.Favourite).Select(m => m.SongId).ToHashSet();
        BrokenIds = _byId.Values.Where(m => m.Broken).Select(m => m.SongId).ToHashSet();
    }
}
