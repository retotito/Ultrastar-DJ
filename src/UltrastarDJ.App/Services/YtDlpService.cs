using System.Diagnostics;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Sidecars;
using UltrastarDJ.Infrastructure;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.Services;

/// <summary>
/// yt-dlp, the helper behind every YouTube song: which version runs, whether a newer one exists (checked once at start
/// when online — never updated by itself, nothing changes mid-party), the update from Settings → YouTube, and the hint
/// the error dialogs add when a newer yt-dlp would likely fix a YouTube failure.
/// </summary>
public sealed class YtDlpService : IDisposable
{
    private readonly MediaService _media;
    private readonly NotificationService _notifications;
    private readonly ConnectivityService _connectivity;
    private readonly ILogger<YtDlpService> _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly YtDlpUpdater _updater;

    public YtDlpService(MediaService media, AppPaths paths, NotificationService notifications, ConnectivityService connectivity, ILogger<YtDlpService> log)
    {
        _media = media;
        _notifications = notifications;
        _connectivity = connectivity;
        _log = log;
        _updater = new YtDlpUpdater(_http, paths);
    }

    /// <summary>UI thread: versions or the update state changed.</summary>
    public event Action? Changed;

    public string? CurrentVersion { get; private set; }
    public string? LatestVersion { get; private set; }
    public bool UpdateAvailable => YtDlpVersion.IsNewer(LatestVersion, CurrentVersion);
    public bool Busy { get; private set; }
    /// <summary>"Checking…", "Updating…", or why it failed; empty otherwise.</summary>
    public string Status { get; private set; } = "";

    /// <summary>At app start: the running version (also pays macOS's first-run scan before the first song), then a quiet check.</summary>
    public async Task StartAsync()
    {
        if (_media.YtDlpPath is not { } path)
        {
            return;
        }

        Stopwatch sw = Stopwatch.StartNew();
        CurrentVersion = await YtDlpUpdater.VersionOfAsync(path).ConfigureAwait(false);
        if (CurrentVersion is null)
        {
            _log.LogWarning("yt-dlp could not be started — YouTube playback will fail.");
        }
        else
        {
            _log.LogInformation("yt-dlp {Version} (started in {Ms} ms)", CurrentVersion, sw.ElapsedMilliseconds);
        }

        Notify();
        if (_connectivity.IsOnline != false && await CheckAsync(quiet: true).ConfigureAwait(false) && UpdateAvailable)
        {
            _notifications.Info(L.T("ytdlp.newer_available"), L.F("ytdlp.newer_available_detail", LatestVersion));
        }
    }

    /// <returns>Whether the check worked.</returns>
    public async Task<bool> CheckAsync(bool quiet = false)
    {
        SetBusy(true, quiet ? "" : L.T("ytdlp.checking"));
        try
        {
            LatestVersion = await _updater.LatestVersionAsync().ConfigureAwait(false);
            _log.LogInformation("yt-dlp: newest release {Latest} (running {Current})", LatestVersion, CurrentVersion);
            SetBusy(false, "");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _log.LogInformation("yt-dlp: update check failed ({Error})", ex.Message);
            SetBusy(false, quiet ? "" : L.T("ytdlp.github_unreachable"));
            return false;
        }
    }

    public async Task UpdateAsync()
    {
        SetBusy(true, L.T("ytdlp.updating"));
        try
        {
            (string path, string version) = await _updater.InstallLatestAsync().ConfigureAwait(false);
            _media.SetYtDlpPath(path);
            _log.LogInformation("yt-dlp updated {Old} → {New} ({Path})", CurrentVersion, version, path);
            CurrentVersion = version;
            LatestVersion ??= version;
            SetBusy(false, "");
            _notifications.Success(L.T("ytdlp.updated"), L.F("ytdlp.updated_detail", version));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or PlatformNotSupportedException)
        {
            _log.LogWarning(ex, "yt-dlp update failed");
            SetBusy(false, L.F("ytdlp.update_failed", ex.Message));
        }
    }

    /// <summary>
    /// The error dialog's reasons, plus a pointer to the update when a newer yt-dlp would likely fix the failure
    /// (PlaybackError.YtDlpMayHelp) — a DJ cannot know that YouTube changed something.
    /// </summary>
    public IReadOnlyList<string> WithHint(IReadOnlyList<string> reasons, bool ytDlpMayHelp)
    {
        if (!ytDlpMayHelp)
        {
            return reasons;
        }

        string hint = UpdateAvailable
            ? L.F("ytdlp.hint_newer", LatestVersion)
            : YtDlpVersion.AgeInDays(CurrentVersion, DateOnly.FromDateTime(DateTime.Now)) is > 30 and var days
                ? L.F("ytdlp.hint_old", days)
                : L.T("ytdlp.hint_check");
        return [.. reasons, hint];
    }

    private void SetBusy(bool busy, string status)
    {
        Busy = busy;
        Status = status;
        Notify();
    }

    private void Notify() => Dispatcher.UIThread.Post(() => Changed?.Invoke());

    public void Dispose() => _http.Dispose();
}
