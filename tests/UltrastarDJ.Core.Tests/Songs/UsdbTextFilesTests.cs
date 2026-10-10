using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class UsdbTextFilesTests
{
    [Fact]
    public void FileName_IsArtistTitleAndId()
        => Assert.Equal("Lady Gaga - Shallow [DUET] [23775].txt", UsdbTextFiles.FileName(23775, "Lady Gaga", "Shallow [DUET]"));

    [Fact]
    public void FileName_ReplacesCharactersNoFileSystemAllows()
        => Assert.Equal("AC_DC - What_ Why_ [12].txt", UsdbTextFiles.FileName(12, "AC/DC", "What? Why*"));

    [Fact]
    public void FileName_LongNamesAreCut_TheIdStays()
    {
        string name = UsdbTextFiles.FileName(9, new string('a', 300), "Title");
        Assert.True(name.Length <= UsdbTextFiles.MaxNameLength);
        Assert.EndsWith(" [9].txt", name);
    }

    [Theory]
    [InlineData("Lady Gaga - Shallow [DUET] [23775].txt", 23775)]
    [InlineData("23775.txt", 23775)]
    [InlineData("AC_DC - What_ Why_ [12].TXT", 12)]
    public void IdOf_ReadsTheId(string file, int id) => Assert.Equal(id, UsdbTextFiles.IdOf(file));

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Song [abc].txt")]
    [InlineData("cover [12].jpg")]
    public void IdOf_OtherFiles_Null(string file) => Assert.Null(UsdbTextFiles.IdOf(file));

    // Catalog: 1 favourite, 2 favourite but changed on USDB since download, 3 not downloaded, 4 downloaded and current.
    private static readonly UsdbTextFiles.Wanted[] Catalog = [new(1, 100), new(2, 500), new(3, 100), new(4, 100)];
    private static readonly Dictionary<int, long> OnDisk = new() { [1] = 200, [2] = 300, [4] = 200 };

    [Fact]
    public void ToFetch_Loaded_Nothing()
        => Assert.Empty(UsdbTextFiles.ToFetch(Catalog, OnDisk, UsdbTextsMode.Loaded, new HashSet<int> { 1, 2 }).Ids);

    [Fact]
    public void ToFetch_Favourites_MissingOrChanged()
    {
        UsdbTextFiles.Plan plan = UsdbTextFiles.ToFetch(Catalog, OnDisk, UsdbTextsMode.Favourites, new HashSet<int> { 1, 2 });
        Assert.Equal([2], plan.Ids);
        Assert.Equal(2, plan.Target);
    }

    [Fact]
    public void ToFetch_All_FavouritesFirst()
    {
        UsdbTextFiles.Plan plan = UsdbTextFiles.ToFetch(Catalog, OnDisk, UsdbTextsMode.All, new HashSet<int> { 3 });
        Assert.Equal([3, 2], plan.Ids);
        Assert.Equal(4, plan.Target);
    }
}
