using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Displays panel: open/close each beamer, toggle its fullscreen, and choose which players sing on it.</summary>
public sealed partial class DisplaysPanelViewModel : ViewModelBase
{
    private readonly IDisplayService _displays;
    private readonly PlayersService _players;

    public DisplaysPanelViewModel(IDisplayService displays, PlayersService players)
    {
        _displays = displays;
        _players = players;
        Beamer1 = new DisplayRowViewModel(DisplayId.Beamer1, _displays, _players, this);
        Beamer2 = new DisplayRowViewModel(DisplayId.Beamer2, _displays, _players, this);
        _displays.OpenStateChanged += OnOpenStateChanged;
        _displays.FullScreenChanged += OnFullScreenChanged;
        // A player that loses its mic (unplugged, or set to none) leaves its beamer and its toggle disables.
        _displays.PlayersChanged += SyncAssignments;
        _players.Changed += _ => SyncAssignments();
    }

    public DisplayRowViewModel Beamer1 { get; }
    public DisplayRowViewModel Beamer2 { get; }

    /// <summary>A player moved to one display → the other display's toggles must follow.</summary>
    internal void SyncAssignments()
    {
        Beamer1.LoadAssignments();
        Beamer2.LoadAssignments();
    }

    private DisplayRowViewModel Row(DisplayId id) => id == DisplayId.Beamer1 ? Beamer1 : Beamer2;

    private void OnOpenStateChanged(DisplayId id, bool isOpen)
    {
        Row(id).IsOpen = isOpen;
        if (!isOpen)
        {
            Row(id).IsFullScreen = false;
        }
    }

    private void OnFullScreenChanged(DisplayId id, bool fullScreen) => Row(id).IsFullScreen = fullScreen;
}

public sealed partial class DisplayRowViewModel : ObservableObject
{
    private readonly IDisplayService _displays;
    private readonly PlayersService _players;
    private readonly DisplaysPanelViewModel _owner;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFullScreenCommand))]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isFullScreen;

    public DisplayRowViewModel(DisplayId id, IDisplayService displays, PlayersService players, DisplaysPanelViewModel owner)
    {
        Id = id;
        _displays = displays;
        _players = players;
        _owner = owner;
        IsOpen = displays.IsOpen(id);
        IsFullScreen = displays.IsFullScreen(id);
        foreach (PlayerConfig p in players.All)
        {
            Players.Add(new PlayerToggleViewModel(p, this));
        }

        LoadAssignments();
    }

    public DisplayId Id { get; }
    public string Title => $"Beamer {(int)Id}";
    public ObservableCollection<PlayerToggleViewModel> Players { get; } = [];

    internal void LoadAssignments()
    {
        IReadOnlyList<int> ids = _displays.GetConfig(Id).PlayerIds;
        foreach (PlayerToggleViewModel t in Players)
        {
            t.SetSilently(ids.Contains(t.Config.Id));
            t.Refresh(_players.Get(t.Config.Id));
        }
    }

    internal void Toggle(int playerId, bool on)
    {
        List<int> ids = [.. _displays.GetConfig(Id).PlayerIds];
        if (on && !ids.Contains(playerId))
        {
            ids.Add(playerId);
        }
        else if (!on)
        {
            ids.Remove(playerId);
        }

        _displays.SetPlayers(Id, ids);
        _owner.SyncAssignments();
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => _displays.Open(Id);

    private bool CanOpen() => !IsOpen;

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close() => _displays.Close(Id);

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void ToggleFullScreen() => _displays.ToggleFullScreen(Id);
}

/// <summary>One player chip on a display row. Disabled when the player has no mic.</summary>
public sealed partial class PlayerToggleViewModel(PlayerConfig config, DisplayRowViewModel row) : ObservableObject
{
    private bool _silent;

    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private PlayerConfig _config = config;

    public string Label => Config.Name;
    public bool HasMic => Config.Mic is not null;
    public string ColorKey => $"BrushPlayer{Config.Id}";

    public void SetSilently(bool on)
    {
        _silent = true;
        IsOn = on;
        _silent = false;
    }

    public void Refresh(PlayerConfig latest)
    {
        Config = latest;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(HasMic));
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_silent)
        {
            row.Toggle(Config.Id, value);
        }
    }
}
