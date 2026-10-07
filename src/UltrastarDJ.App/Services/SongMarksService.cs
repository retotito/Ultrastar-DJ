using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.Services;

/// <summary>
/// The DJ's marks on songs — favourite, broken (with a note) — kept in <c>settings/marks.json</c>, so a backup of the
/// settings folder carries them. Follows local songs whose folder moved (<see cref="SongMarks.Reattach"/>). UI thread.
/// </summary>
public sealed class SongMarksService
{
    private const string SettingsName = "marks";

    private readonly ISettingsStore _settings;
    private readonly LibraryService _library;
    private readonly ILogger<SongMarksService> _log;
    private readonly SongMarks _marks;

    public SongMarksService(ISettingsStore settings, LibraryService library, ILogger<SongMarksService> log)
    {
        _settings = settings;
        _library = library;
        _log = log;
        _marks = new SongMarks(settings.Load(SettingsName, new MarksDocument([])).Marks);
        // The library may change on a scan thread.
        library.Changed += () => Dispatcher.UIThread.Post(Reattach);
        Reattach();
    }

    /// <summary>A mark was set, changed or removed: the library redraws, the songbook re-sends its catalog.</summary>
    public event Action? Changed;

    public SongMark? For(Song song) => _marks.For(song);
    public IReadOnlySet<string> FavouriteIds => _marks.FavouriteIds;
    public IReadOnlySet<string> BrokenIds => _marks.BrokenIds;

    public void SetFavourite(Song song, bool favourite) => Change(song, m => m with { Favourite = favourite });
    public void SetBroken(Song song, bool broken) => Change(song, m => m with { Broken = broken });
    public void SetNote(Song song, string? note) => Change(song, m => m with { Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });

    private void Change(Song song, Func<SongMark, SongMark> change)
    {
        SongMark? before = _marks.For(song);
        SongMark? after = _marks.Set(song, change);
        if (before == after)
        {
            return;
        }

        Save();
        // Typing a note changes the mark per key: log only what the DJ switched.
        if (before?.Favourite != after?.Favourite || before?.Broken != after?.Broken)
        {
            _log.LogInformation("{Song}: {Marks}", song.Id, after is null ? "marks removed"
                : $"{(after.Favourite ? "favourite " : "")}{(after.Broken ? "broken" : "")}".Trim());
        }

        Changed?.Invoke();
    }

    private void Reattach()
    {
        if (_marks.Reattach(_library.Songs))
        {
            _log.LogInformation("Song marks moved to songs whose folder moved");
            Save();
            Changed?.Invoke();
        }
    }

    private void Save() => _settings.Save(SettingsName, new MarksDocument([.. _marks.All]));

    public sealed record MarksDocument(IReadOnlyList<SongMark> Marks);
}
