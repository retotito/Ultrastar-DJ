using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using UltrastarDJ.App.Services;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

/// <summary>File manager, browser and clipboard are view concerns: the Details popup's buttons live here.</summary>
public sealed partial class SongDetailsView : UserControl
{
    /// <summary>The popup's width in English; other languages may need more for the footer's buttons.</summary>
    private const double BaseWidth = 876;

    public SongDetailsView() => InitializeComponent();

    // The width follows the footer only (a long song text must not widen it): its buttons side by side, at least the
    // English width, at most what the window offers.
    protected override Size MeasureOverride(Size availableSize)
    {
        Footer.Measure(Size.Infinity);
        double needed = Footer.DesiredSize.Width + Card.Padding.Left + Card.Padding.Right;
        double width = Math.Max(BaseWidth, Math.Ceiling(needed));
        if (double.IsFinite(availableSize.Width))
        {
            width = Math.Min(width, availableSize.Width);
        }

        if (Math.Abs(Card.Width - width) > 0.5)
        {
            Card.Width = width;
        }

        return base.MeasureOverride(availableSize);
    }

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
