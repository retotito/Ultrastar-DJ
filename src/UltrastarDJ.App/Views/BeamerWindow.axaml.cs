using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace UltrastarDJ.App.Views;

/// <summary>
/// Singer screen. A normal window the DJ drags to the projector; double-click or F toggles fullscreen there,
/// Esc only leaves fullscreen (closing is the title bar's or the Displays panel's job, never a stray key).
/// In fullscreen the mouse pointer hides after a moment without movement.
/// </summary>
public sealed partial class BeamerWindow : Window
{
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private readonly DispatcherTimer _hideCursor = new() { Interval = TimeSpan.FromSeconds(2) };

    public BeamerWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        DoubleTapped += (_, _) => ToggleFullScreen();
        PointerMoved += (_, _) => ShowCursorForAWhile();
        _hideCursor.Tick += (_, _) =>
        {
            _hideCursor.Stop();
            if (WindowState == WindowState.FullScreen)
            {
                Cursor = HiddenCursor;
            }
        };
        Closed += (_, _) => _hideCursor.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            ShowCursorForAWhile();
        }
    }

    private void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private void ShowCursorForAWhile()
    {
        Cursor = Cursor.Default;
        _hideCursor.Stop();
        if (WindowState == WindowState.FullScreen)
        {
            _hideCursor.Start();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.Escape when WindowState == WindowState.FullScreen:
                WindowState = WindowState.Normal;
                e.Handled = true;
                break;
        }
    }
}
