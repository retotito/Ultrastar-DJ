using Avalonia;

namespace UltrastarDJ.App;

internal static class Program
{
    // Avalonia configuration must stay in this method: the designer and previewer call it via reflection.
    [STAThread]
    public static int Main(string[] args)
    {
        // First thing: Velopack's install / update / uninstall hooks run here and may exit the process.
        Velopack.VelopackApp.Build().Run();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Linux (X11): Avalonia answers the desktop session manager's "save yourself" with a shutdown request —
            // on Linux Mint (cinnamon-session) the app closed by itself mid-song. Settings are saved as they change,
            // so nothing is lost at a real log-out.
            .With(new X11PlatformOptions { EnableSessionManagement = false })
            .WithInterFont()
            .LogToTrace();
}
