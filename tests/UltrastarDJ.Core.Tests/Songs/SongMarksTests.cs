using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongMarksTests
{
    private static Song Local(string path, string title = "Roxanne", string artist = "The Police")
        => new() { Id = $"a::{path}", SourceId = "a", Title = title, Artist = artist, Bpm = 300, TxtPath = path };

    private static Song Usdb(int id, string title = "Roxanne", string artist = "The Police")
        => new() { Id = $"usdb::{id}", SourceId = "usdb", Title = title, Artist = artist, Bpm = 300, UsdbId = id };

    [Fact]
    public void Set_Favourite_IsFoundAgain()
    {
        SongMarks marks = new([]);
        Song song = Local("/Music/Police/Roxanne.txt");
        marks.Set(song, m => m with { Favourite = true });

        Assert.True(marks.For(song)?.Favourite);
        Assert.Contains(song.Id, marks.FavouriteIds);
    }

    [Fact]
    public void Set_BrokenWithNote_KeepsTheNote()
    {
        SongMarks marks = new([]);
        Song song = Usdb(42);
        marks.Set(song, m => m with { Broken = true, Note = "lyrics 1 s late" });

        Assert.Equal(("lyrics 1 s late", true), (marks.For(song)!.Note, marks.For(song)!.Broken));
        Assert.Contains(song.Id, marks.BrokenIds);
    }

    [Fact]
    public void Set_NothingLeft_RemovesTheMark()
    {
        SongMarks marks = new([]);
        Song song = Usdb(42);
        marks.Set(song, m => m with { Favourite = true });
        marks.Set(song, m => m with { Favourite = false });

        Assert.Null(marks.For(song));
        Assert.Empty(marks.All);
    }

    [Fact]
    public void Set_NoteOnly_IsKept()
        // A note is worth keeping even after "broken" was switched off (e.g. "fixed the GAP").
        => Assert.NotNull(new SongMarks([]).Set(Usdb(1), m => m with { Note = "fixed GAP" }));

    [Fact]
    public void Reattach_LocalSongMovedFolder_FindsItByArtistAndTitle()
    {
        // The id of a local song contains its path: after moving the folder the mark must follow the song.
        Song before = Local("/Volumes/Old/Police/Roxanne.txt");
        SongMarks marks = new([]);
        marks.Set(before, m => m with { Favourite = true });

        Song after = Local("/Volumes/New/Police/Roxanne.txt");
        bool changed = marks.Reattach([after]);

        Assert.True(changed);
        Assert.True(marks.For(after)?.Favourite);
        Assert.Equal(after.Id, Assert.Single(marks.All).SongId);
    }

    [Fact]
    public void Reattach_IgnoresCaseAndSpaces()
    {
        SongMarks marks = new([]);
        marks.Set(Local("/old/x.txt", "Roxanne", "The Police"), m => m with { Broken = true });
        marks.Reattach([Local("/new/x.txt", " roxanne", "the police ")]);

        Assert.Single(marks.BrokenIds, "a::/new/x.txt");
    }

    [Fact]
    public void Reattach_SongStillThere_LeavesTheMarkAlone()
    {
        // Two copies of a song in two folders: the marked one is still present — the other copy stays unmarked.
        Song marked = Local("/a/Roxanne.txt");
        Song copy = Local("/b/Roxanne.txt");
        SongMarks marks = new([]);
        marks.Set(marked, m => m with { Favourite = true });

        Assert.False(marks.Reattach([marked, copy]));
        Assert.Null(marks.For(copy));
    }

    [Fact]
    public void Reattach_NeverMovesUsdbMarks()
    {
        // USDB ids are stable; a USDB song that left the catalog keeps its mark for when it comes back.
        SongMarks marks = new([]);
        marks.Set(Usdb(42), m => m with { Favourite = true });

        Assert.False(marks.Reattach([Local("/x/Roxanne.txt")]));
        Assert.Equal("usdb::42", Assert.Single(marks.All).SongId);
    }

    [Fact]
    public void Reattach_TargetAlreadyMarked_KeepsBoth()
    {
        Song gone = Local("/old/Roxanne.txt");
        Song present = Local("/new/Roxanne.txt");
        SongMarks marks = new([]);
        marks.Set(gone, m => m with { Favourite = true });
        marks.Set(present, m => m with { Broken = true });

        Assert.False(marks.Reattach([present]));
        Assert.Equal(2, marks.All.Count);
        Assert.False(marks.For(present)!.Favourite);
    }

    [Fact]
    public void Reattach_OnlyWithinTheSameSource()
    {
        // A source switched off takes its songs out of the library: its marks must not jump to a copy elsewhere.
        SongMarks marks = new([]);
        marks.Set(Local("/a/Roxanne.txt"), m => m with { Favourite = true });
        Song otherSource = new() { Id = "b::/b/Roxanne.txt", SourceId = "b", Title = "Roxanne", Artist = "The Police", Bpm = 300 };

        Assert.False(marks.Reattach([otherSource]));
        Assert.Null(marks.For(otherSource));
    }
}
