using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class DuetDetectionTests
{
    private const string Header = "#TITLE:Shallow\n#ARTIST:Bradley Cooper & Lady Gaga\n#BPM:300\n";

    [Theory]
    [InlineData("P1\n: 0 4 5 Tell\nP2\n: 20 4 5 Tell\nE\n")]   // newer files
    [InlineData("P 1\n: 0 4 5 Tell\nP 2\n: 20 4 5 Tell\nE\n")] // older files
    [InlineData("P1\r\n: 0 4 5 Tell\r\nP2\r\n: 20 4 5 Tell\r\nE\r\n")] // Windows line ends
    public void LocalFile_WithASecondVoice_IsADuet(string body)
        => Assert.True(UltraStarParser.ParseSong("/x/a.txt", "a", Header + body)!.IsDuet);

    [Fact]
    public void LocalFile_OneVoice_IsNot()
        => Assert.False(UltraStarParser.ParseSong("/x/a.txt", "a", Header + ": 0 4 5 Tell\n: 4 4 5 me\nE\n")!.IsDuet);

    [Fact]
    public void LocalFile_OnlyP1_IsNot()
        // Some solo files carry a lone "P1" marker.
        => Assert.False(UltraStarParser.ParseSong("/x/a.txt", "a", Header + "P1\n: 0 4 5 Tell\nE\n")!.IsDuet);

    [Fact]
    public void LocalFile_LyricsSayingP2_IsNot()
        // A syllable "P2" is part of a note line, never a line of its own.
        => Assert.False(UltraStarParser.ParseSong("/x/a.txt", "a", Header + ": 0 4 5 P2\nE\n")!.IsDuet);

    [Theory]
    [InlineData("Shallow [DUET]", true)]
    [InlineData("A Whole New World (Album Version) [duet]", true)]
    [InlineData("Shallow", false)]
    [InlineData("Duet for one", false)]
    public void UsdbSong_MarkedDuetInTheTitle(string title, bool duet)
        => Assert.Equal(duet, new UsdbCatalogEntry { SongId = 1, Title = title, Artist = "A" }.ToSong().IsDuet);
}
