using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Infrastructure.Library;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Turns a library row into a playable <see cref="Song"/>: USDB rows get their txt (network/cache) and
/// YouTube id, every song is validated, local songs get their notes parsed. Throws <see cref="SongLoadException"/>.
/// </summary>
public sealed class SongResolver(UsdbService usdb)
{
    private readonly SongValidator _validator = new(new FileSystemExistence());

    public async Task<Song> ResolveAsync(Song song, CancellationToken ct = default)
    {
        if (song.UsdbId is { } usdbId)
        {
            song = await ResolveUsdbAsync(song, usdbId, ct).ConfigureAwait(false);
        }

        SongValidationResult v = _validator.Validate(song);
        if (!v.IsValid)
        {
            throw new SongLoadException(string.Join("\n", v.Errors.Select(e => e.Message)));
        }

        Song loaded = v.Song.Notes is null
            ? await Task.Run(() => SongNotesLoader.WithNotes(v.Song), ct).ConfigureAwait(false)
            : v.Song;
        if (loaded.Notes is null || loaded.Notes.Count == 0)
        {
            throw new SongLoadException("Song has no notes.");
        }

        return loaded;
    }

    private async Task<Song> ResolveUsdbAsync(Song song, int usdbId, CancellationToken ct)
    {
        string txt;
        try
        {
            txt = await usdb.GetSongTxtAsync(usdbId, ct).ConfigureAwait(false);
        }
        catch (UsdbException ex)
        {
            throw new SongLoadException(ex.Message);
        }
        catch (IOException ex)
        {
            throw new SongLoadException($"Cannot cache the USDB song text: {ex.Message}");
        }

        SongHeader h = UltraStarParser.ParseHeader(txt);
        if (h.YouTubeId is null)
        {
            throw new SongLoadException("This USDB song has no YouTube link — nothing to play.");
        }

        return song with
        {
            Bpm = h.Bpm ?? 0,
            GapMs = h.GapMs ?? 0,
            YouTubeId = h.YouTubeId,
            VideoGapSec = h.VideoGapSec,
            StartSec = h.StartSec,
            EndMs = h.EndMs,
            Notes = UltraStarParser.ParseNotes(txt),
        };
    }
}
