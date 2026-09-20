using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Infrastructure.Library;

/// <summary>Recursively parses every <c>.txt</c> under a folder into <see cref="Song"/> headers.</summary>
public sealed class LocalFolderScanner(ILogger<LocalFolderScanner> log)
{
    public sealed record Progress(int Found, int Parsed);

    public async Task<IReadOnlyList<Song>> ScanAsync(string sourceId, string folder, IProgress<Progress>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder))
        {
            log.LogWarning("Source folder missing: {Folder}", folder);
            return [];
        }

        return await Task.Run(() =>
        {
            List<Song> songs = [];
            int found = 0;
            EnumerationOptions opts = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
            foreach (string txt in Directory.EnumerateFiles(folder, "*.txt", opts))
            {
                ct.ThrowIfCancellationRequested();
                found++;
                try
                {
                    string text = File.ReadAllText(txt);
                    Song? song = UltraStarParser.ParseSong(txt, sourceId, text);
                    if (song is not null)
                    {
                        songs.Add(song);
                    }
                }
                catch (IOException ex)
                {
                    log.LogDebug(ex, "Skipping unreadable {File}", txt);
                }
                catch (UnauthorizedAccessException ex)
                {
                    log.LogDebug(ex, "Skipping inaccessible {File}", txt);
                }

                if (found % 50 == 0)
                {
                    progress?.Report(new Progress(found, songs.Count));
                }
            }

            progress?.Report(new Progress(found, songs.Count));
            log.LogInformation("Scanned {Folder}: {Songs} songs from {Files} txt files", folder, songs.Count, found);
            return (IReadOnlyList<Song>)songs;
        }, ct).ConfigureAwait(false);
    }
}
