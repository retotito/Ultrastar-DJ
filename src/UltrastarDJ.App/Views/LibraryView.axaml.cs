using Avalonia.Controls;
using Avalonia.Input;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

public sealed partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        SongList.DoubleTapped += async (_, _) => await LoadAsync();
        SongList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await LoadAsync();
            }
        };
    }

    private Task LoadAsync() => DataContext is LibraryViewModel vm ? vm.LoadSelectedAsync() : Task.CompletedTask;
}
