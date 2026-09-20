using Avalonia.Controls;
using Avalonia.Input;

namespace UltrastarDJ.App.Views;

public sealed partial class BeamerWindow : Window
{
    public BeamerWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
