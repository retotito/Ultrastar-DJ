using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using UltrastarDJ.App.Services;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

/// <summary>File manager, browser and clipboard are view concerns: the Details popup's buttons live here.</summary>
public sealed partial class SongDetailsView : UserControl
{
    public SongDetailsView() => InitializeComponent();

    private SongDetailsViewModel? Vm => DataContext as SongDetailsViewModel;

    private void OnShowInFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is { TxtPath: { } txt })
        {
            FileReveal.Reveal(txt);
        }
    }

    private async void OnOpenUsdb(object? sender, RoutedEventArgs e) => await OpenAsync(Vm?.UsdbUrl);

    private async void OnOpenYouTube(object? sender, RoutedEventArgs e) => await OpenAsync(Vm?.YouTubeUrl);

    private async Task OpenAsync(string? url)
    {
        if (url is not null && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private async void OnCopyTxt(object? sender, RoutedEventArgs e)
    {
        if (Vm is { HasTxt: true } vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(vm.Txt);
        }
    }
}
