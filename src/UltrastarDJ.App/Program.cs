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
            .WithInterFont()
            .LogToTrace();
}
