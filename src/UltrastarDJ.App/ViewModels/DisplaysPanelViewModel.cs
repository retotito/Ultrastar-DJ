using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Displays panel: pick a monitor per beamer and open/close it. Player assignment arrives with Sprint 3.</summary>
public sealed partial class DisplaysPanelViewModel : ViewModelBase
{
    private readonly IDisplayService _displays;

    public DisplaysPanelViewModel(IDisplayService displays)
    {
        _displays = displays;
        _displays.RefreshScreens();
        Screens = new ObservableCollection<ScreenInfo>(_displays.Screens);
        Beamer1 = new DisplayRowViewModel(DisplayId.Beamer1, _displays, Screens);
        Beamer2 = new DisplayRowViewModel(DisplayId.Beamer2, _displays, Screens);
        _displays.OpenStateChanged += OnOpenStateChanged;
    }

    public ObservableCollection<ScreenInfo> Screens { get; }
    public DisplayRowViewModel Beamer1 { get; }
    public DisplayRowViewModel Beamer2 { get; }

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
    private readonly ObservableCollection<ScreenInfo> _screens;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private ScreenInfo? _selectedScreen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpen;

    public DisplayRowViewModel(DisplayId id, IDisplayService displays, ObservableCollection<ScreenInfo> screens)
    {
        Id = id;
        _displays = displays;
        _screens = screens;
        IsOpen = displays.IsOpen(id);
        PickDefaultScreen();
    }

    public DisplayId Id { get; }
    public string Title => $"Beamer {(int)Id}";

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
