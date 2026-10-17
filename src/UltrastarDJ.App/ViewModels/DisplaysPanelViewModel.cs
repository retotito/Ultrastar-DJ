using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Players;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Displays panel: open/close each beamer, toggle its fullscreen, and choose which players sing on it.</summary>
public sealed partial class DisplaysPanelViewModel : ViewModelBase
{
    private readonly IDisplayService _displays;
    private readonly PlayersService _players;

    private readonly DjWindowViewModel _window;

    public DisplaysPanelViewModel(IDisplayService displays, PlayersService players, DjWindowViewModel window)
    {
        _window = window;
        _displays = displays;
        _players = players;
        Beamer1 = new DisplayRowViewModel(DisplayId.Beamer1, _displays, _players, this);
        Beamer2 = new DisplayRowViewModel(DisplayId.Beamer2, _displays, _players, this);
        _displays.OpenStateChanged += OnOpenStateChanged;
        _displays.FullScreenChanged += OnFullScreenChanged;
        _displays.PlacementChanged += id => Row(id).RefreshScreen();
        // A player that loses its mic (unplugged, or set to none) leaves its beamer and its toggle disables.
        _displays.PlayersChanged += SyncAssignments;
        _players.Changed += _ => SyncAssignments();
        _singleScreenMode = displays.SingleScreenMode;
        _displays.SingleScreenChanged += OnSingleScreenChanged;
    }

    // ── Single-screen mode ──

    public IReadOnlyList<SingleScreenMode> SingleScreenModes { get; } = [SingleScreenMode.Automatic, SingleScreenMode.On, SingleScreenMode.Off];

    [ObservableProperty] private SingleScreenMode _singleScreenMode;

    partial void OnSingleScreenModeChanged(SingleScreenMode value)
    {
        if (value != _displays.SingleScreenMode)
        {
            _displays.SetSingleScreenMode(value);
        }
    }

    /// <summary>"1 screen connected", plus "— changes after this song" while a change waits (the badge shows the state).</summary>
    public string SingleScreenStatus
    {
        get
        {
            string screens = L.F(_displays.ScreenCount == 1 ? "single.one_screen" : "single.screens", _displays.ScreenCount);
            bool wanted = Core.Displays.SingleScreen.IsActive(SingleScreenMode, _displays.ScreenCount);
            return _displays.IsSingleScreen == wanted ? screens : $"{screens} — {L.T("single.after_song")}";
        }
    }

    /// <summary>The card's badge: Active (orange) / Off (grey).</summary>
    public bool IsSingleScreenActive => _displays.IsSingleScreen;

    /// <summary>The ⓘ button: the explanation with its step-by-step picture (shown by the DJ window).</summary>
    [RelayCommand]
    private void ShowSingleScreenInfo() => _window.SingleScreenInfoOpen = true;

    private void OnSingleScreenChanged()
    {
        SingleScreenMode = _displays.SingleScreenMode;
        OnPropertyChanged(nameof(SingleScreenStatus));
        OnPropertyChanged(nameof(IsSingleScreenActive));
        Beamer1.RefreshSingleScreen();
        Beamer2.RefreshSingleScreen();
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

        Row(id).RefreshScreen();
    }

    private void OnFullScreenChanged(DisplayId id, bool fullScreen) => Row(id).IsFullScreen = fullScreen;
}

public sealed partial class DisplayRowViewModel : ObservableObject
{
    private readonly IDisplayService _displays;
    private readonly PlayersService _players;
    private readonly DisplaysPanelViewModel _owner;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleFullScreenCommand))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isFullScreen;

    /// <summary>The screen the window is on (null while closed).</summary>
    [ObservableProperty] private string? _screen;

    /// <summary>The status pill: Closed (grey) / Open (green) / Fullscreen (blue).</summary>
    public string StateText => !IsOpen ? L.T("state.closed") : IsFullScreen ? L.T("state.fullscreen") : L.T("state.open");

    internal void RefreshScreen() => Screen = _displays.ScreenText(Id);

    /// <summary>Display 2 while single-screen mode is on: cannot open; its players are shown on Display 1.</summary>
    public bool IsUnused => _displays.IsSingleScreen && Id == DisplayId.Beamer2;
    public bool CanOpen => !IsUnused;

    /// <summary>Display 1 while single-screen mode is on: it opens by itself for each song — no Open / Fullscreen.</summary>
    public bool IsAutomatic => _displays.IsSingleScreen && Id == DisplayId.Beamer1;
    public bool ShowsButtons => !IsAutomatic;
    public bool ShowsScreen => !IsAutomatic && !IsUnused;

    internal void RefreshSingleScreen()
    {
        OnPropertyChanged(nameof(IsUnused));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(IsAutomatic));
        OnPropertyChanged(nameof(ShowsButtons));
        OnPropertyChanged(nameof(ShowsScreen));
        ToggleOpenCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The screen it is on, or where a closed one will open.</summary>
    public string ScreenText => Screen ?? L.T("displays.opens_next");

    partial void OnScreenChanged(string? value) => OnPropertyChanged(nameof(ScreenText));

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(StateText));

    public DisplayRowViewModel(DisplayId id, IDisplayService displays, PlayersService players, DisplaysPanelViewModel owner)
    {
        Id = id;
        _displays = displays;
        _players = players;
        _owner = owner;
        IsOpen = displays.IsOpen(id);
        IsFullScreen = displays.IsFullScreen(id);
        Screen = displays.ScreenText(id);
        foreach (PlayerConfig p in players.All)
        {
            Players.Add(new PlayerToggleViewModel(p, this));
        }

        LoadAssignments();
    }

    public DisplayId Id { get; }
    public string Title => L.F("display.label", (int)Id);
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

    /// <summary>The panel's one Open / Close button.</summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void ToggleOpen()
    {
        if (IsOpen)
        {
            _displays.Close(Id);
        }
        else
        {
            _displays.Open(Id);
        }
    }

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
