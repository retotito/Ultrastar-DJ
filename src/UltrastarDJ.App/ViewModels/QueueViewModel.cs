using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Queue;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.ViewModels;

/// <summary>The DJ's queue plus incoming guest requests. Wraps <see cref="Playlist"/>; loading a song into the game marks it active.</summary>
public sealed partial class QueueViewModel : ViewModelBase
{
    private readonly Playlist _playlist;
    private readonly NowPlayingViewModel _nowPlaying;
    private readonly SongbookService _songbook;

    [ObservableProperty] private Song? _selected;

    public QueueViewModel(Playlist playlist, NowPlayingViewModel nowPlaying, SongbookService songbook)
    {
        _playlist = playlist;
        _nowPlaying = nowPlaying;
        _songbook = songbook;
        _playlist.Changed += Refresh;
        _songbook.Changed += () => OnPropertyChanged(nameof(HasRequests));
        Refresh();
    }

    public ObservableCollection<QueueRowViewModel> Items { get; } = [];
    public ObservableCollection<SongRequest> Requests => _songbook.Requests;
    public bool HasRequests => _songbook.Requests.Count > 0;
    public int Count => _playlist.Count;
    public bool IsEmpty => _playlist.Count == 0;

    public void Add(Song song) => _playlist.Add(song);

    [RelayCommand] private void AcceptRequest(SongRequest? r) { if (r is not null) { _songbook.Accept(r); } }
    [RelayCommand] private void DismissRequest(SongRequest? r) { if (r is not null) { _songbook.Dismiss(r); } }

    private void Refresh()
    {
        Items.Clear();
        for (int i = 0; i < _playlist.Items.Count; i++)
        {
            Items.Add(new QueueRowViewModel(_playlist.Items[i], i + 1, i == _playlist.ActiveIndex, this));
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        LoadNextCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    internal void Remove(Song s) => _playlist.Remove(s.Id);
    internal void MoveUp(Song s) => _playlist.MoveUp(s.Id);
    internal void MoveDown(Song s) => _playlist.MoveDown(s.Id);

    internal async Task LoadAsync(Song s)
    {
        await _nowPlaying.LoadCommand.ExecuteAsync(s);
        if (_nowPlaying.HasSong)
        {
            _playlist.SetActive(s.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadNext))]
    private Task LoadNextAsync() => _playlist.Next is { } next ? LoadAsync(next) : Task.CompletedTask;
    private bool CanLoadNext() => _playlist.Next is not null;

    [RelayCommand(CanExecute = nameof(HasItems))]
    private void Clear() => _playlist.Clear();
    private bool HasItems() => _playlist.Count > 0;
}

public sealed partial class QueueRowViewModel(Song song, int number, bool isActive, QueueViewModel owner) : ObservableObject
{
    public Song Song { get; } = song;
    public int Number { get; } = number;
    public bool IsActive { get; } = isActive;
    public string Title => Song.Title;
    public string Artist => Song.Artist;

    [RelayCommand] private Task LoadAsync() => owner.LoadAsync(Song);
    [RelayCommand] private void Remove() => owner.Remove(Song);
    [RelayCommand] private void MoveUp() => owner.MoveUp(Song);
    [RelayCommand] private void MoveDown() => owner.MoveDown(Song);
}
