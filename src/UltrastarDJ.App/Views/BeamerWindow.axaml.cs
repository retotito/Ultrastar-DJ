using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

/// <summary>
/// Singer screen. A normal window the DJ drags to the projector; double-click or F toggles fullscreen there,
/// Esc only leaves fullscreen (closing is the title bar's or the Displays panel's job, never a stray key).
/// In fullscreen the mouse pointer hides after a moment without movement.
/// Single-screen mode (the display in front of the DJ window): moving the mouse during a song shows the control bar;
/// Space pauses / continues, Esc pauses and shows the DJ window; on the score screen a click or key goes back to it.
/// </summary>
public sealed partial class BeamerWindow : Window
{
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    // The pointer and the single-screen control bar hide together after this long without movement.
    private readonly DispatcherTimer _hideCursor = new() { Interval = TimeSpan.FromSeconds(3) };
    // What fullscreen came from: leaving it returns there (maximised on the projector stays maximised), not to the
    // small window the display first opened as.
    private WindowState _beforeFullScreen = WindowState.Normal;

    public BeamerWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        DoubleTapped += (_, _) => ToggleFullScreen();
        PointerMoved += (_, _) => ShowCursorForAWhile();
        _hideCursor.Tick += (_, _) =>
        {
            // Not while the DJ is about to click it.
            if (ControlBar.IsPointerOver)
            {
                return;
            }

            _hideCursor.Stop();
            ControlBar.IsVisible = false;
            if (WindowState == WindowState.FullScreen)
            {
                Cursor = HiddenCursor;
            }
        };
        PointerPressed += OnPointerPressed;
        Closed += (_, _) => _hideCursor.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            if (change.GetNewValue<WindowState>() == WindowState.FullScreen && change.GetOldValue<WindowState>() is var before and not WindowState.FullScreen and not WindowState.Minimized)
            {
                _beforeFullScreen = before;
            }

            ShowCursorForAWhile();
        }
    }

    /// <summary>Into fullscreen, or back to what it was before (maximised or the normal window).</summary>
    public void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.FullScreen ? _beforeFullScreen : WindowState.FullScreen;

    private BeamerViewModel? Vm => DataContext as BeamerViewModel;

    private void ShowCursorForAWhile()
    {
        Cursor = Cursor.Default;
        _hideCursor.Stop();
        ControlBar.IsVisible = Vm?.HasControls == true;
        if (WindowState == WindowState.FullScreen || ControlBar.IsVisible)
        {
            _hideCursor.Start();
        }
    }

    // Single-screen mode, score screen: a click goes back to the DJ window (the timer would after a while).
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is { IsSingleScreen: true, IsScore: true } vm)
        {
            vm.BackToDjCommand.Execute(null);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is { IsSingleScreen: true } vm && OnSingleScreenKey(vm, e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.Escape when WindowState == WindowState.FullScreen:
                WindowState = _beforeFullScreen;
                e.Handled = true;
                break;
        }
    }

    private static bool OnSingleScreenKey(BeamerViewModel vm, Key key)
    {
        if (vm.IsScore)
        {
            vm.BackToDjCommand.Execute(null);
            return true;
        }

        switch (key)
        {
            case Key.Space when vm.HasControls:
                vm.TogglePauseCommand.Execute(null);
                return true;
            case Key.Escape:
                vm.BackToDjCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }
}
