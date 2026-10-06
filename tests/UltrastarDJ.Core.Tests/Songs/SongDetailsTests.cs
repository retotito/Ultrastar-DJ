using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongSummaryTests
{
    private static Song S(double bpm = 300, double gapMs = 1000) => new() { Id = "a::x", SourceId = "a", Title = "T", Artist = "A", Bpm = bpm, GapMs = gapMs, Language = "English", AudioPath = "/s/a.mp3" };

    private static NoteTrack Track(params Note[] notes) => new(0, [new LyricLine(0, notes)]);

    [Theory]
    [InlineData(0, "C4")]
    [InlineData(-3, "A3")]
    [InlineData(13, "C#5")]
    [InlineData(-13, "B2")]
    public void NoteName_UltraStarPitchZeroIsC4(int usPitch, string name)
    {
        Assert.Equal(name, SongSummary.NoteName(usPitch));
    }

    [Fact]
    public void Of_CountsTypesAndRangeIgnoringRapAndFreestyle()
    {
        NoteTrack t = new(0,
        [
            new LyricLine(0, [new Note(NoteType.Normal, 0, 4, -5, "a"), new Note(NoteType.Golden, 4, 4, 7, "b")]),
            new LyricLine(10, [new Note(NoteType.Rap, 10, 2, 30, "c"), new Note(NoteType.Freestyle, 12, 2, -20, "d")]),
        ]);

        SongSummary s = SongSummary.Of(S(), [t]);

        Assert.Equal((2, 4, 1, 1), (s.Phrases, s.Notes, s.RapNotes, s.FreestyleNotes));
        Assert.Equal(4.0 / 10, s.GoldenShare, 6);      // golden 4 of 10 scorable beats (4 + 4 + rap 2)
        Assert.Equal(("G3", "G4"), (s.LowestNote, s.HighestNote));
        Assert.False(s.IsDuet);
    }

    [Fact]
    public void Of_SingingEndsAtTheLastNoteInSeconds()
    {
        // BPM 300: a beat is 60 / 1200 = 0.05 s; last note ends at beat 20 → 1.0 s + gap 1.0 s.
        SongSummary s = SongSummary.Of(S(), [Track(new Note(NoteType.Normal, 0, 20, 0, "a"))]);

        Assert.Equal(2.0, s.SingingEndsSec, 6);
    }

    [Fact]
    public void Of_TwoTracksIsADuet()
    {
        Assert.True(SongSummary.Of(S(), [Track(new Note(NoteType.Normal, 0, 1, 0, "a")), Track(new Note(NoteType.Normal, 2, 1, 0, "b"))]).IsDuet);
    }
}

public class SongCheckTests
{
    private sealed class Files(params string[] existing) : IFileExistence
    {
        public bool Exists(string path) => existing.Contains(path);
        public string? ReadText(string path) => null;
    }

    private static Song S() => new() { Id = "a::x", SourceId = "a", Title = "T", Artist = "A", Bpm = 300, GapMs = 0, Language = "English", AudioPath = "/s/a.mp3" };

    private static NoteTrack Track(params Note[] notes) => new(0, [new LyricLine(0, notes)]);

    [Fact]
    public void CleanSong_HasNoProblems()
    {
        Assert.Empty(SongCheck.Problems(S(), [Track(new Note(NoteType.Normal, 0, 4, 0, "a"))], new Files("/s/a.mp3")));
    }

    [Fact]
    public void MissingCover_IsReportedByFileName()
    {
        Assert.Contains("Cover not found: c.jpg", SongCheck.Problems(S() with { CoverPath = "/s/c.jpg" }, null, new Files("/s/a.mp3")));
    }

    [Fact]
    public void MissingAudio_IsReportedOnce()
    {
        IReadOnlyList<string> p = SongCheck.Problems(S(), null, new Files());

        Assert.Single(p, x => x.Contains("a.mp3", StringComparison.Ordinal));
    }

    [Fact]
    public void VideoGapWithoutVideo()
    {
        Assert.Contains("#VIDEOGAP is set, but the song has no video", SongCheck.Problems(S() with { VideoGapSec = 1.5 }, null, new Files("/s/a.mp3")));
    }

    [Fact]
    public void NoLanguage()
    {
        Assert.Contains(SongCheck.Problems(S() with { Language = null }, null, new Files("/s/a.mp3")), p => p.StartsWith("No #LANGUAGE", StringComparison.Ordinal));
    }

    [Fact]
    public void OverlappingAndZeroLengthNotes()
    {
        NoteTrack t = Track(new Note(NoteType.Normal, 0, 4, 0, "a"), new Note(NoteType.Normal, 2, 4, 0, "b"), new Note(NoteType.Normal, 8, 0, 0, "c"));

        IReadOnlyList<string> p = SongCheck.Problems(S(), [t], new Files("/s/a.mp3"));

        Assert.Contains("Notes overlap at beat 2", p);
        Assert.Contains("1 note has length 0", p);
    }

    [Fact]
    public void StartAfterTheLastNote()
    {
        // Last note ends at beat 4 = 0.2 s.
        Assert.Contains("#START is after the last note — nothing is sung",
            SongCheck.Problems(S() with { StartSec = 5 }, [Track(new Note(NoteType.Normal, 0, 4, 0, "a"))], new Files("/s/a.mp3")));
    }
}
