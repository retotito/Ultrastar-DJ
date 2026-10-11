using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Localization;
using UltrastarDJ.App.Views;
using Velopack;
using Velopack.Sources;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Updates from the GitHub Releases page (docs/06-build-release.md "Updates"): a check shortly after the start; a
/// newer version is offered ("Update now" / "Later") once no song is on; "Update now" downloads it with progress and
/// restarts into it. Settings and songs live outside the app and stay. Only an installed app updates — the developer
/// build (dotnet run) has nothing to replace and never checks.
/// </summary>
public sealed partial class UpdateService : ObservableObject
{
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(5);

    private readonly ILogger<UpdateService> _log;
    private readonly UpdateManager _manager = new(new GithubSource(AboutWindow.ProjectUrl, null, prerelease: true));
    private UpdateInfo? _update;

    public UpdateService(ILogger<UpdateService> log)
    {
        _log = log;
    }

    /// <summary>The newer version found ("0.2.0-beta.2"); null while there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    private string? _newVersion;

    /// <summary>The DJ chose "Later": not offered again until the next start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    private bool _postponed;

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string? _error;

    /// <summary>An update waits for the DJ's answer.</summary>
    public bool IsAvailable => NewVersion is not null && !Postponed;

    public string CurrentVersion => BuildInfo.Version;

    public async Task CheckAfterStartAsync()
    {
        if (!_manager.IsInstalled)
        {
            return;
        }

        await Task.Delay(CheckDelay).ConfigureAwait(false);
        try
        {
            UpdateInfo? update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                _log.LogInformation("Updates: {Version} is the newest", BuildInfo.Version);
                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            _log.LogInformation("Updates: {New} available (running {Current})", version, BuildInfo.Version);
            Dispatcher.UIThread.Post(() =>
            {
                _update = update;
                NewVersion = version;
            });
        }
        catch (Exception ex)
        {
            // Offline, GitHub unreachable, rate-limited: the next start asks again. Nothing for the DJ to do.
            _log.LogWarning(ex, "Updates: check failed");
        }
    }

    public void Later() => Postponed = true;

    /// <summary>Downloads the new version and restarts into it. On failure the offer stays, with the reason.</summary>
    public async Task UpdateNowAsync()
    {
        if (_update is not { } update || IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        Error = null;
        Progress = 0;
        try
        {
            await _manager.DownloadUpdatesAsync(update, p => Dispatcher.UIThread.Post(() => Progress = p)).ConfigureAwait(true);
            _log.LogInformation("Updates: {Version} downloaded — restarting into it", NewVersion);
            _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Updates: download or install failed");
            Error = L.F("update.failed", ex.Message);
            IsDownloading = false;
        }
    }
}
