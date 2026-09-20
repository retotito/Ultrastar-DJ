using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Infrastructure.Library;

/// <summary>Real file system for the validator.</summary>
public sealed class FileSystemExistence : IFileExistence
{
    public bool Exists(string path) => File.Exists(path);

    public string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Loads the note body of a song on demand (library rows carry headers only).</summary>
public static class SongNotesLoader
{
    public static Song WithNotes(Song song)
    {
        if (song.Notes is not null || song.TxtPath is null)
        {
            return song;
        }

        string text = File.ReadAllText(song.TxtPath);
        return song with { Notes = UltraStarParser.ParseNotes(text) };
    }
}
