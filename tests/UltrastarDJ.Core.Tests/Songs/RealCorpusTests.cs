using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

/// <summary>Parses every real .txt under $ULTRASTAR_SONGS (skipped when unset) — a corpus smoke test.</summary>
public class RealCorpusTests
{
    [Fact]
    public void EveryTxtInCorpus_ParsesWithNotes()
    {
        string? root = Environment.GetEnvironmentVariable("ULTRASTAR_SONGS");
        if (root is null || !Directory.Exists(root))
        {
            return;
        }

        List<string> problems = [];
        int count = 0;
        foreach (string txt in Directory.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories))
        {
            count++;
            string text = File.ReadAllText(txt);
            Song? song = UltraStarParser.ParseSong(txt, "corpus", text);
            if (song is null)
            {
                problems.Add($"{txt}: no title/artist");
                continue;
            }

            IReadOnlyList<NoteTrack> tracks = UltraStarParser.ParseNotes(text);
            int notes = tracks.Sum(t => t.AllNotes.Count());
            if (notes == 0)
            {
                problems.Add($"{txt}: no notes");
            }

            if (!song.HasLocalAudio && !song.HasLocalVideo && !song.HasYouTube)
            {
                problems.Add($"{txt}: no media reference");
            }
        }

        Assert.True(count > 0, "corpus is empty");
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }
}
