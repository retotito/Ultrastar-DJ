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
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Infrastructure;
using UltrastarDJ.Infrastructure.Settings;

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
        desktop.Exit += (_, _) => Shutdown();

        DjWindow window = new() { DataContext = _services.GetRequiredService<DjWindowViewModel>() };
        _services.GetRequiredService<DisplayService>().AttachOwner(window);
        desktop.MainWindow = window;

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

        // App services
        services.AddSingleton<MediaService>();
        services.AddSingleton<DisplayService>();
        services.AddSingleton<IDisplayService>(sp => sp.GetRequiredService<DisplayService>());

        // ViewModels
        services.AddSingleton<DjWindowViewModel>();
        services.AddTransient<DisplaysPanelViewModel>();
        services.AddSingleton<MediaLabPanelViewModel>();

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

    private void Shutdown()
    {
        // Native players must go before the container tears down loggers.
        if (_services?.GetService<MediaService>() is { } media)
        {
            media.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _services?.Dispose();
        Log.CloseAndFlush();
    }
}
