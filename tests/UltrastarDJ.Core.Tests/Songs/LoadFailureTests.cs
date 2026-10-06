using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class LoadFailureTests
{
    private static readonly DateTime T1 = new(2026, 10, 6, 18, 0, 0, DateTimeKind.Utc);

    private static Song Local(string txt = "/s/a.txt") => new() { Id = "a::x", SourceId = "a", Title = "T", Artist = "A", Bpm = 300, TxtPath = txt };

    private static Song Usdb(long mtime) => new UsdbCatalogEntry { SongId = 42, Title = "T", Artist = "A", UsdbMtime = mtime }.ToSong();

    [Fact]
    public void Usdb_FingerprintIsTheChangeTime()
    {
        Assert.Equal("usdb:1700000000", LoadFailure.FingerprintOf(Usdb(1_700_000_000), _ => null));
    }

    [Fact]
    public void Local_FingerprintIsTheTxtModificationTime()
    {
        Assert.Equal($"txt:{T1.Ticks}", LoadFailure.FingerprintOf(Local(), _ => T1));
        Assert.Equal("txt:missing", LoadFailure.FingerprintOf(Local(), _ => null));
    }

    [Fact]
    public void FixedOnUsdb_NoLongerApplies()
    {
        LoadFailure f = new("usdb::42", "no YouTube link", T1, LoadFailure.FingerprintOf(Usdb(100), _ => null));

        Assert.True(f.AppliesTo(Usdb(100), _ => null));
        Assert.False(f.AppliesTo(Usdb(200), _ => null));
    }

    [Fact]
    public void EditedLocally_NoLongerApplies()
    {
        LoadFailure f = new("a::x", "no notes", T1, LoadFailure.FingerprintOf(Local(), _ => T1));

        Assert.True(f.AppliesTo(Local(), _ => T1));
        Assert.False(f.AppliesTo(Local(), _ => T1.AddMinutes(5)));
    }
}
