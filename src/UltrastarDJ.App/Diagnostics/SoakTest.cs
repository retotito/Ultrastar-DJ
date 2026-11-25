#if DEBUG
using System.Diagnostics;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Services;
using UltrastarDJ.App.ViewModels;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Playback;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.Diagnostics;

/// <summary>
/// A long unattended run that uses YouTube like a busy DJ (issue #5, YouTube's "not a bot" pause at long parties):
/// a random YouTube song into the preview every 30 s, and every 3.5 min a song into the Game Player and played — every
/// second time the one just previewed (one lookup for both, the cache). Everything goes through the same view models
/// as the DJ's clicks. Each step, every error dialog (then closed) and a summary every 15 min go to the log
/// ("Soak:" lines); the run ends after the given hours. Developer builds only:
/// <c>USDJ_SOAK=3 dotnet run --project src/UltrastarDJ.App</c> (hours; on a Mac with <c>caffeinate -dims</c> in front).
/// </summary>
public static class SoakTest
{
    private static readonly TimeSpan PreviewEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GameEvery = TimeSpan.FromMinutes(3.5);
    private static readonly TimeSpan SummaryEvery = TimeSpan.FromMinutes(15);

    private static int _previews, _previewFails, _games, _gameFails, _dialogs;
    private static DateTime? _firstPause;

    public static void StartIfAsked(IServiceProvider services)
    {
        if (Environment.GetEnvironmentVariable("USDJ_SOAK") is not { Length: > 0 } value)
        {
            return;
        }

        double hours = double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double h) && h > 0 ? h : 3;
        _ = Dispatcher.UIThread.InvokeAsync(() => RunAsync(services, TimeSpan.FromHours(hours)));
    }

    private static async Task RunAsync(IServiceProvider sp, TimeSpan duration)
    {
        ILogger log = sp.GetRequiredService<ILogger<App>>();
        LibraryService library = sp.GetRequiredService<LibraryService>();
        PreviewViewModel preview = sp.GetRequiredService<PreviewViewModel>();
        NowPlayingViewModel nowPlaying = sp.GetRequiredService<NowPlayingViewModel>();
        PlaybackService playback = sp.GetRequiredService<PlaybackService>();
        MediaService media = sp.GetRequiredService<MediaService>();
        NotificationService notifications = sp.GetRequiredService<NotificationService>();
        IDisplayService displays = sp.GetRequiredService<IDisplayService>();

        // Let the app settle (USDB login, connectivity) as a DJ would before the first song.
        await Task.Delay(TimeSpan.FromSeconds(20));
        Random random = new();
        Stopwatch run = Stopwatch.StartNew();
        DateTime nextGame = DateTime.UtcNow + TimeSpan.FromMinutes(1), nextSummary = DateTime.UtcNow + SummaryEvery;
        log.LogInformation("Soak: start — {Hours} h, preview every {Preview} s, Game Player every {Game} min",
            duration.TotalHours, PreviewEvery.TotalSeconds, GameEvery.TotalMinutes);

        while (run.Elapsed < duration)
        {
            List<Song> pool = [.. library.Songs.Where(s => (s.HasYouTube || s.UsdbId is not null) && library.IsAvailable(s.SourceId))];
            if (pool.Count == 0)
            {
                log.LogWarning("Soak: no YouTube songs available (USDB offline?) — waiting");
                await Task.Delay(PreviewEvery);
                continue;
            }

            Song previewSong = pool[random.Next(pool.Count)];
            await StepAsync(log, notifications, media, "preview", previewSong, async () =>
            {
                await preview.LoadAsync(previewSong);
                return media.Preview.State is MediaState.Playing or MediaState.Ready;
            });

            if (DateTime.UtcNow >= nextGame)
            {
                nextGame = DateTime.UtcNow + GameEvery;
                Song gameSong = _games % 2 == 0 ? previewSong : pool[random.Next(pool.Count)];
                if (!playback.AnyDisplayOpen)
                {
                    displays.Open(DisplayId.Beamer1);
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }

                playback.Stop();
                await StepAsync(log, notifications, media, gameSong.Id == previewSong.Id ? "game (just previewed)" : "game", gameSong, async () =>
                {
                    await nowPlaying.LoadCommand.ExecuteAsync(gameSong);
                    if (playback.Song?.Id != gameSong.Id)
                    {
                        return false;
                    }

                    await playback.PlayAsync();
                    return true;
                }, game: true);
            }

            if (DateTime.UtcNow >= nextSummary)
            {
                nextSummary = DateTime.UtcNow + SummaryEvery;
                Summary(log, media, run.Elapsed, "summary");
            }

            await Task.Delay(PreviewEvery);
        }

        playback.Stop();
        Summary(log, media, run.Elapsed, "done");
    }

    private static async Task StepAsync(ILogger log, NotificationService notifications, MediaService media, string what, Song song,
        Func<Task<bool>> step, bool game = false)
    {
        Stopwatch sw = Stopwatch.StartNew();
        int lookupsBefore = media.YouTube.LookupCount;
        bool ok;
        try
        {
            ok = await step();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Soak: {What} threw", what);
            ok = false;
        }

        int asked = media.YouTube.LookupCount - lookupsBefore;
        if (game)
        {
            _games++;
            _gameFails += ok ? 0 : 1;
        }
        else
        {
            _previews++;
            _previewFails += ok ? 0 : 1;
        }

        log.LogInformation("Soak: {What} {Result} in {Ms} ms, YouTube asked {Asked}× — {Artist} – {Title}",
            what, ok ? "ok" : "FAILED", sw.ElapsedMilliseconds, asked, song.Artist, song.Title);

        if (media.YouTube.IsPaused && _firstPause is null)
        {
            _firstPause = DateTime.Now;
            log.LogWarning("Soak: YouTube paused (\"not a bot\") after {Lookups} lookups, {Previews} previews, {Games} game loads",
                media.YouTube.LookupCount, _previews, _games);
        }

        // What the DJ would have seen — logged, then closed so the next step starts clean.
        while (notifications.Dialog is { } dialog)
        {
            _dialogs++;
            log.LogWarning("Soak: dialog \"{Title}\": {Reasons}", dialog.Title, string.Join(" / ", dialog.Reasons));
            notifications.DismissDialog();
        }
    }

    private static void Summary(ILogger log, MediaService media, TimeSpan elapsed, string kind)
    {
        log.LogInformation(
            "Soak {Kind} after {Elapsed:hh\\:mm}: previews {Previews} ({PreviewFails} failed), game loads {Games} ({GameFails} failed), " +
            "YouTube lookups {Lookups}, dialogs {Dialogs}, first pause {Pause}, paused now {Paused}",
            kind, elapsed, _previews, _previewFails, _games, _gameFails, media.YouTube.LookupCount, _dialogs,
            _firstPause?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "none", media.YouTube.IsPaused);
    }
}
#endif
