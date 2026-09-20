using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Displays panel: pick a monitor per beamer, open/close it, and choose which players sing on it.</summary>
public sealed partial class DisplaysPanelViewModel : ViewModelBase
{
    private readonly IDisplayService _displays;
    private readonly PlayersService _players;

    public DisplaysPanelViewModel(IDisplayService displays, PlayersService players)
    {
        _displays = displays;
        _players = players;
        _displays.RefreshScreens();
        Screens = new ObservableCollection<ScreenInfo>(_displays.Screens);
        Beamer1 = new DisplayRowViewModel(DisplayId.Beamer1, _displays, _players, Screens, this);
        Beamer2 = new DisplayRowViewModel(DisplayId.Beamer2, _displays, _players, Screens, this);
        _displays.OpenStateChanged += OnOpenStateChanged;
    }

    public ObservableCollection<ScreenInfo> Screens { get; }
    public DisplayRowViewModel Beamer1 { get; }
    public DisplayRowViewModel Beamer2 { get; }

    /// <summary>A player moved to one display → the other display's toggles must follow.</summary>
    internal void SyncAssignments()
    {
        Beamer1.LoadAssignments();
        Beamer2.LoadAssignments();
    }

    [RelayCommand]
    private void RefreshScreens()
    {
        _displays.RefreshScreens();
        Screens.Clear();
        foreach (ScreenInfo s in _displays.Screens)
        {
            Screens.Add(s);
        }

        Beamer1.PickDefaultScreen();
        Beamer2.PickDefaultScreen();
    }

    private void OnOpenStateChanged(DisplayId id, bool isOpen)
    {
        (id == DisplayId.Beamer1 ? Beamer1 : Beamer2).IsOpen = isOpen;
    }
}

public sealed partial class DisplayRowViewModel : ObservableObject
{
    private readonly IDisplayService _displays;
    private readonly PlayersService _players;
    private readonly ObservableCollection<ScreenInfo> _screens;
    private readonly DisplaysPanelViewModel _owner;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private ScreenInfo? _selectedScreen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpen;

    public DisplayRowViewModel(DisplayId id, IDisplayService displays, PlayersService players, ObservableCollection<ScreenInfo> screens, DisplaysPanelViewModel owner)
    {
        Id = id;
        _displays = displays;
        _players = players;
        _screens = screens;
        _owner = owner;
        IsOpen = displays.IsOpen(id);
        PickDefaultScreen();
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

    /// <summary>Prefer the persisted screen, then the first non-primary one, then whatever exists.</summary>
    public void PickDefaultScreen()
    {
        string? remembered = _displays.GetConfig(Id).ScreenName;
        SelectedScreen = _screens.FirstOrDefault(s => s.Name == remembered)
            ?? _screens.FirstOrDefault(s => !s.IsPrimary)
            ?? _screens.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => _displays.Open(Id, SelectedScreen!);

    private bool CanOpen() => !IsOpen && SelectedScreen is not null;

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close() => _displays.Close(Id);
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
