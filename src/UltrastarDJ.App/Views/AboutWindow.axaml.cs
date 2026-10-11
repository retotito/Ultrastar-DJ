using Avalonia.Controls;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.Views;

/// <summary>macOS app menu → About Ultrastar DJ: name, version, project page. One window at a time.</summary>
public sealed partial class AboutWindow : Window
{
    public const string ProjectUrl = "https://github.com/retotito/Ultrastar-DJ";

    private static AboutWindow? _open;

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = L.F("about.version", BuildInfo.Version);
        Website.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(ProjectUrl));
    }

    public static void Open(Window? owner)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        _open = new AboutWindow();
        _open.Closed += (_, _) => _open = null;
        if (owner is not null)
        {
            _open.Show(owner);
        }
        else
        {
            _open.Show();
        }
    }
}
