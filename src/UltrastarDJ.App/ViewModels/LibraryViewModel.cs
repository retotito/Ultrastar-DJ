using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Centre panel: the song library. Sprint 2 shows a plain virtualized list; search/filter/sort follow in Sprint 5.</summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly NowPlayingViewModel _nowPlaying;
    private readonly MediaService _media;
    private readonly ILogger<LibraryViewModel> _log;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private Song? _selected;
    [ObservableProperty] private Song? _previewing;
    [ObservableProperty] private string _previewError = "";

    public LibraryViewModel(LibraryService library, NowPlayingViewModel nowPlaying, MediaService media, ILogger<LibraryViewModel> log)
    {
        _library = library;
        _nowPlaying = nowPlaying;
        _media = media;
        _log = log;
        Refresh();
        _library.Changed += () => Dispatcher.UIThread.Post(Refresh);
    }

    public ObservableCollection<Song> Songs { get; } = [];
    public Media.FrameBus PreviewFrames => _media.Preview.Frames;
    private static readonly SongValidator Validator = new(new Infrastructure.Library.FileSystemExistence());

    /// <summary>Right-click → "Load into game" or the row's play icon.</summary>
    [RelayCommand]
    private Task LoadIntoGameAsync(Song? song) => song is null ? Task.CompletedTask : _nowPlaying.LoadCommand.ExecuteAsync(song);

    /// <summary>Double-click: preview in the DJ's headphones (preview channel, 360p for YouTube).</summary>
    [RelayCommand]
    private async Task PreviewAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        PreviewError = "";
        Previewing = song;
        try
        {
            SongValidationResult v = Validator.Validate(song);
            if (!v.IsValid)
            {
                PreviewError = string.Join("\n", v.Errors.Select(e => e.Message));
                return;
            }

            song = v.Song;
            MediaPlan plan = MediaSourceResolver.Resolve(new SongMedia
            {
                AudioPath = song.AudioPath,
                VideoPath = song.VideoPath,
                YouTubeId = song.YouTubeId,
                BackgroundPath = song.BackgroundPath,
                CoverPath = song.CoverPath,
                VideoGapSec = song.VideoGapSec ?? 0,
            }, maxHeight: 360);
            await _media.Preview.LoadAsync(plan);
            _media.Preview.Play();
        }
        catch (MediaException ex)
        {
            PreviewError = ex.Message;
            _log.LogWarning("Preview failed: {Error}", ex.Message);
        }
    }

    [RelayCommand]
    private void TogglePreview()
    {
        if (_media.Preview.State == Media.MediaState.Playing)
        {
            _media.Preview.Pause();
        }
        else
        {
            _media.Preview.Play();
        }
    }

    [RelayCommand]
    private async Task StopPreviewAsync()
    {
        await _media.Preview.UnloadAsync();
        Previewing = null;
    }

    public Task LoadSelectedAsync() => PreviewAsync(Selected);

    partial void OnSearchChanged(string value) => Refresh();

    private void Refresh()
    {
        IReadOnlyList<Song> all = _library.Songs;
        TotalCount = all.Count;
        string q = Search.Trim();
        IEnumerable<Song> filtered = q.Length == 0
            ? all
            : all.Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Artist.Contains(q, StringComparison.OrdinalIgnoreCase));

        Songs.Clear();
        foreach (Song s in filtered)
        {
            Songs.Add(s);
        }
    }
}
