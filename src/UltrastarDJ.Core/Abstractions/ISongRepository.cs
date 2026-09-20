using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Abstractions;

/// <summary>A place songs come from. <see cref="Path"/> is set for local folders.</summary>
public sealed record SongSource(string Id, SongSourceType Type, string Label, string? Path, bool Enabled = true)
{
    public static SongSource LocalFolder(string path)
        => new(Guid.NewGuid().ToString("N"), SongSourceType.LocalFolder, System.IO.Path.GetFileName(path.TrimEnd('/', '\\')), path);
}

public enum SongSourceType
{
    LocalFolder,
    Usdb,
}

/// <summary>Library persistence. Implementations are safe to call from any thread; queries return snapshots.</summary>
public interface ISongRepository
{
    /// <summary>Replaces every song of <paramref name="sourceId"/> with <paramref name="songs"/> in one transaction.</summary>
    void ReplaceSource(string sourceId, IReadOnlyList<Song> songs);
    void RemoveSource(string sourceId);
    IReadOnlyList<Song> GetAll();
    int CountBySource(string sourceId);
}
