namespace UltrastarDJ.Core.Songs;

/// <summary>
/// A song that could not be loaded for a reason of its own (no YouTube link, video removed, broken file) — marked in
/// the library until it loads again or changes. Connection trouble is never recorded: the song itself is fine.
/// </summary>
/// <param name="SongId"><see cref="Song.Id"/>.</param>
/// <param name="Reason">The sentence the DJ saw.</param>
/// <param name="AtUtc">When it failed.</param>
/// <param name="Fingerprint">The song's version when it failed (<see cref="FingerprintOf"/>); another version clears the mark.</param>
public sealed record LoadFailure(string SongId, string Reason, DateTime AtUtc, string Fingerprint)
{
    /// <summary>
    /// What identifies this version of the song: USDB's change time, or the local .txt's modification time. A song
    /// fixed on USDB or edited locally gets a new fingerprint, so an old failure no longer applies.
    /// </summary>
    /// <param name="song">The song.</param>
    /// <param name="lastWriteUtc">File modification time, or null if the file is missing (file-system seam).</param>
    public static string FingerprintOf(Song song, Func<string, DateTime?> lastWriteUtc)
    {
        if (song.UsdbId is not null)
        {
            return $"usdb:{song.UsdbMtime ?? 0}";
        }

        return song.TxtPath is { } txt && lastWriteUtc(txt) is { } t ? $"txt:{t.Ticks}" : "txt:missing";
    }

    /// <summary>Still about this version of the song.</summary>
    public bool AppliesTo(Song song, Func<string, DateTime?> lastWriteUtc) => FingerprintOf(song, lastWriteUtc) == Fingerprint;
}
