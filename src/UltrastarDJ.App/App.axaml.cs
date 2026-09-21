using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using UltrastarDJ.App.Services;
using UltrastarDJ.App.ViewModels;
using UltrastarDJ.App.Views;
using UltrastarDJ.Audio;
using UltrastarDJ.Audio.PortAudio;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Infrastructure;
using UltrastarDJ.Infrastructure.Library;
using UltrastarDJ.Infrastructure.Settings;
using UltrastarDJ.Infrastructure.Usdb;

namespace UltrastarDJ.App;

/// <summary>Composition root. The only place that knows every concrete type.</summary>
public sealed partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppOptions options = AppOptions.Parse(desktop.Args ?? []);
        AppPaths paths = new();
        ConfigureSerilog(paths);

        _services = BuildServices(options, paths);
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.ShutdownRequested += OnShutdownRequested;
        desktop.Exit += (_, _) => Log.CloseAndFlush();

        DjWindow window = new() { DataContext = _services.GetRequiredService<DjWindowViewModel>() };
        _services.GetRequiredService<DisplayService>().AttachOwner(window);
        // Applies the persisted output routing to the media channels before anything plays, and the theme.
        _services.GetRequiredService<OutputsService>();
        _services.GetRequiredService<AppSettingsService>();
        desktop.MainWindow = window;
        // Reconnects to USDB with saved credentials and pulls catalog changes; failures only set the panel status.
        _ = _services.GetRequiredService<UsdbService>().AutoConnectAsync();

        _services.GetRequiredService<ILogger<App>>().LogInformation(
            "Ultrastar DJ {Version} started (beamer-debug: {BeamerDebug})",
            typeof(App).Assembly.GetName().Version?.ToString(3), options.BeamerDebug);

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServices(AppOptions options, AppPaths paths)
    {
        ServiceCollection services = new();

        services.AddSingleton(options);
        services.AddSingleton(paths);
        services.AddLogging(builder => builder.AddSerilog(dispose: false));

        // Infrastructure
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<SidecarLocator>();
        services.AddSingleton<ISongRepository, SqliteSongRepository>();
        services.AddSingleton<IUsdbCatalog, SqliteUsdbCatalog>();
        services.AddSingleton<IUsdbClient, UsdbClient>();
        services.AddSingleton<LocalFolderScanner>();

        // App services
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<MediaService>();
        services.AddSingleton<IAudioBackend, PortAudioBackend>();
        services.AddSingleton<PlayersService>();
        services.AddSingleton<AudioInputService>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<UsdbService>();
        services.AddSingleton<SongResolver>();
        services.AddSingleton<OutputsService>();
        services.AddSingleton<PlaybackService>();
        services.AddSingleton<DisplayService>();
        services.AddSingleton<IDisplayService>(sp => sp.GetRequiredService<DisplayService>());

        // ViewModels
        services.AddSingleton<DjWindowViewModel>();
        services.AddSingleton<Core.Queue.Playlist>();
        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<QueueViewModel>();
        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SourcesPanelViewModel>();
        services.AddSingleton<PlayersPanelViewModel>();
        services.AddSingleton<AudioOutputPanelViewModel>();
        services.AddSingleton<SettingsPanelViewModel>();
        services.AddTransient<DisplaysPanelViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void ConfigureSerilog(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext:l}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(paths.Logs, "ultrastardj-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext:l}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private bool _shuttingDown;

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shuttingDown || sender is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        // Native players and audio streams are torn down on a worker while the dispatcher keeps pumping:
        // frame/state callbacks post to the UI thread, so blocking it here would deadlock the teardown.
        _shuttingDown = true;
        e.Cancel = true;
        _ = DisposeServicesThenExitAsync(desktop);
    }

    private async Task DisposeServicesThenExitAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ServiceProvider services = _services!;
        ILogger<App> log = services.GetRequiredService<ILogger<App>>();
        Task dispose = Task.Run(() => services.DisposeAsync().AsTask());
        Task finished = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5)));
        if (finished != dispose)
        {
            log.LogWarning("Shutdown: service teardown did not finish in time; exiting anyway");
        }
        else if (dispose.IsFaulted)
        {
            log.LogError(dispose.Exception, "Shutdown: service teardown failed");
        }

        desktop.Shutdown();
        // Hung native threads (mpv, PortAudio) must not keep the process alive.
        Log.CloseAndFlush();
        Environment.Exit(0);
    }
}
