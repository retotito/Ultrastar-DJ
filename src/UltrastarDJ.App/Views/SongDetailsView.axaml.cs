using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

/// <summary>File manager, browser and clipboard are view concerns: the Details popup's buttons live here.</summary>
public sealed partial class SongDetailsView : UserControl
{
    public SongDetailsView() => InitializeComponent();

    private SongDetailsViewModel? Vm => DataContext as SongDetailsViewModel;

    private async void OnShowInFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { TxtPath: { } txt, Folder: { } folder })
        {
            return;
        }

        // Reveal selects the .txt in its folder; the launcher can only open the folder itself.
        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", ["-R", txt]) { UseShellExecute = false });
        }
        else if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{txt}\"") { UseShellExecute = false });
        }
        else if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
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
