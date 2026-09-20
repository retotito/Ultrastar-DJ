using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class UltraStarParserTests
{
    private const string Sample = """
        #TITLE:Test Song
        #ARTIST:Tester
        #BPM:300,5
        #GAP:16528
        #MP3:song.mp3
        #VIDEO:clip.mp4
        #COVER:co.jpg
        #VIDEOGAP:19,5
        #END:120000
        #YEAR:1999
        #LANGUAGE:English
        : -466 4 5 Hel
        : -460 4 7 lo 
        *  0 8 12 world
        - 10
        F 12 4 0 free
        R 16 4 0 rap
        G 20 4 0 grap
        E
        """;

    [Fact]
    public void ParseHeader_HandlesCommaDecimalsAndUnits()
    {
        SongHeader h = UltraStarParser.ParseHeader(Sample);

        Assert.Equal("Test Song", h.Title);
        Assert.Equal(300.5, h.Bpm);
        Assert.Equal(16528, h.GapMs);
        Assert.Equal(19.5, h.VideoGapSec);
        Assert.Equal(120000, h.EndMs);
        Assert.Equal(1999, h.Year);
        Assert.Equal("clip.mp4", h.Video);
        Assert.Null(h.YouTubeId);
    }

    [Theory]
    [InlineData("#VIDEO:https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("#VIDEO:https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("#YOUTUBE:dQw4w9WgXcQ")]
    [InlineData("#VIDEO:dQw4w9WgXcQ")]
    public void ParseHeader_VideoTagWithYouTube_BecomesYouTubeId(string tag)
    {
        SongHeader h = UltraStarParser.ParseHeader($"#TITLE:a\n#ARTIST:b\n{tag}\n");

        Assert.Equal("dQw4w9WgXcQ", h.YouTubeId);
        Assert.Null(h.Video);
    }

    [Fact]
    public void ParseHeader_AudioTagIsAliasForMp3()
    {
        Assert.Equal("x.m4a", UltraStarParser.ParseHeader("#AUDIO:x.m4a\n").Mp3);
    }

    [Fact]
    public void ParseHeader_RelativeIsDetected()
    {
        Assert.True(UltraStarParser.ParseHeader("#RELATIVE:yes\n").Relative);
    }

    [Fact]
    public void ParseNotes_KeepsNegativeBeatsAndSyllableSpaces()
    {
        IReadOnlyList<NoteTrack> tracks = UltraStarParser.ParseNotes(Sample);

        NoteTrack t = Assert.Single(tracks);
        Assert.Equal(2, t.Lines.Count);
        LyricLine first = t.Lines[0];
        Assert.Equal(-466, first.Notes[0].StartBeat);
        Assert.Equal(-466, first.FirstNoteBeat);
        Assert.Equal(0, first.StartBeat); // parser default for the first line — that is why FirstNoteBeat exists
        Assert.Equal("Hel", first.Notes[0].Syllable);
        Assert.Equal("lo ", first.Notes[1].Syllable); // trailing space = word boundary
        Assert.Equal(NoteType.Golden, first.Notes[2].Type);
        Assert.Equal(12, first.Notes[2].UsPitch);
    }

    [Fact]
    public void ParseNotes_SecondLineStartsAtLineBreakBeat_AndParsesAllTypes()
    {
        LyricLine second = UltraStarParser.ParseNotes(Sample)[0].Lines[1];

        Assert.Equal(10, second.StartBeat);
        Assert.Equal([NoteType.Freestyle, NoteType.Rap, NoteType.RapGolden], second.Notes.Select(n => n.Type));
    }

    [Fact]
    public void ParseNotes_Duet_ProducesTwoTracks()
    {
        const string duet = "P1\n: 0 2 0 a\n- 4\nP2\n: 8 2 0 b\nE\n";

        IReadOnlyList<NoteTrack> tracks = UltraStarParser.ParseNotes(duet);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(0, tracks[0].Player);
        Assert.Equal(1, tracks[1].Player);
        Assert.Equal("b", tracks[1].Lines[0].Notes[0].Syllable);
    }

    [Fact]
    public void ParseNotes_CrLf_DoesNotLeakCarriageReturnIntoSyllable()
    {
        IReadOnlyList<NoteTrack> tracks = UltraStarParser.ParseNotes(": 0 2 0 hi\r\n: 2 2 0 there\r\nE\r\n");

        Assert.Equal("hi", tracks[0].Lines[0].Notes[0].Syllable);
    }

    [Fact]
    public void ParseSong_ResolvesSiblingPathsAndFiltersVideoExtensions()
    {
        string txt = Path.Combine(Path.GetTempPath(), "songs", "A - B", "a.txt");

        Song? song = UltraStarParser.ParseSong(txt, "src1", Sample);

        Assert.NotNull(song);
        Assert.Equal("src1::" + txt, song.Id);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(txt)!, "song.mp3"), song.AudioPath);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(txt)!, "clip.mp4"), song.VideoPath);
        Assert.Equal(19.5, song.VideoGapSec);
    }

    [Fact]
    public void ParseSong_MissingTitle_ReturnsNull()
    {
        Assert.Null(UltraStarParser.ParseSong("/x.txt", "s", "#ARTIST:only\n"));
    }

    [Fact]
    public void ParseSong_DefaultsBpmWhenMissing()
    {
        Assert.Equal(120, UltraStarParser.ParseSong("/x.txt", "s", "#TITLE:t\n#ARTIST:a\n")!.Bpm);
    }
}
