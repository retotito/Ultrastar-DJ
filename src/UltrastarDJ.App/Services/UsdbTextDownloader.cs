using System.Globalization;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Downloads the USDB song texts the DJ wants offline (<see cref="UsdbTextStore.Mode"/>: favourites, or every song) in
/// the background while the app is open — one song every <see cref="Gap"/>, so a volunteer-run server is not hammered
/// (28 000 songs ≈ 12 h; nothing to wait for, it picks up where it stopped at the next start). Only what is missing or
/// changed on USDB since it was fetched is downloaded, so after a sync it fetches just the new and changed songs.
/// Waits while offline, logged out, paused or while the catalog syncs.
/// </summary>
public sealed class UsdbTextDownloader : IDisposable
{
    /// <summary>Between two downloads: about 2 400 songs an hour.</summary>
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(1.5);
    /// <summary>After a network failure, before trying again.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly UsdbService _usdb;
    private readonly UsdbTextStore _texts;
    private readonly IUsdbCatalog _catalog;
    private readonly SongMarksService _marks;
    private readonly ConnectivityService _connectivity;
    private readonly ILogger<UsdbTextDownloader> _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0);
    // Songs USDB returned no text for (deleted there): not asked again this session.
    private readonly HashSet<int> _skipped = [];
    // Bumped by everything that changes what to download: the running plan is dropped and made again.
    private int _version;

    public UsdbTextDownloader(UsdbService usdb, UsdbTextStore texts, IUsdbCatalog catalog, SongMarksService marks, ConnectivityService connectivity,
        ILogger<UsdbTextDownloader> log)
    {
        _usdb = usdb;
        _texts = texts;
        _catalog = catalog;
        _marks = marks;
        _connectivity = connectivity;
        _log = log;
        usdb.Changed += Wake;
        texts.Changed += Wake;
        marks.Changed += Wake;
        connectivity.Changed += _ => Wake();
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Progress changed (background thread; view models marshal).</summary>
    public event Action? Changed;

    /// <summary>How many texts the mode wants on disk (0 = nothing to download in this mode).</summary>
    public int Target { get; private set; }
    /// <summary>How many of those are on disk and current.</summary>
    public int Done { get; private set; }
    /// <summary>Downloading right now (not waiting, not finished).</summary>
    public bool IsActive { get; private set; }
    public double Percent => Target > 0 ? Math.Min(100, 100.0 * Done / Target) : 100;
    /// <summary>"0.4 %" in the first tenth (whole numbers would sit at 0 % for an hour), then "43 %"; never 100 % before the end.</summary>
    public string PercentText
    {
        get
        {
            double p = IsComplete ? 100 : Math.Min(99.9, Percent);
            return p < 10 ? $"{Math.Floor(p * 10) / 10:0.0} %" : $"{Math.Floor(p):0} %";
        }
    }
    public bool IsComplete => Target > 0 && Done >= Target;
    /// <summary>At the current pace, for what is left.</summary>
    public TimeSpan Remaining => Gap * Math.Max(0, Target - Done);

    private void Wake()
    {
        Interlocked.Increment(ref _version);
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }

    private bool CanRun => _texts.Mode != UsdbTextsMode.Loaded && !_texts.Paused && _usdb.IsConnected && !_usdb.IsSyncing
        && _connectivity.IsOnline is not false;

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int version = Volatile.Read(ref _version);
            try
            {
                UsdbTextFiles.Plan plan = _texts.Mode == UsdbTextsMode.Loaded ? new([], 0) : MakePlan();
                Report(plan.Target, plan.Target - plan.Ids.Count, active: false);
                if (CanRun && plan.Ids.Count > 0)
                {
                    _log.LogInformation("USDB song texts: {Count} to download ({Mode})", plan.Ids.Count, _texts.Mode);
                    await DownloadAsync(plan, version, ct).ConfigureAwait(false);
                    if (Volatile.Read(ref _version) != version)
                    {
                        continue;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is UsdbException or IOException or HttpRequestException)
            {
                _log.LogWarning("USDB song texts: download paused ({Error}) — trying again in a minute", ex.Message);
                Report(Target, Done, active: false);
                await WaitAsync(RetryAfter, ct).ConfigureAwait(false);
                continue;
            }

            Report(Target, Done, active: false);
            await _wake.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private UsdbTextFiles.Plan MakePlan()
    {
        HashSet<int> favourites = [.. _marks.FavouriteIds.Select(UsdbIdOf).OfType<int>()];
        IEnumerable<UsdbTextFiles.Wanted> catalog = _catalog.GetAll().Where(e => !_skipped.Contains(e.SongId)).Select(e => new UsdbTextFiles.Wanted(e.SongId, e.UsdbMtime));
        return UsdbTextFiles.ToFetch(catalog, _texts.OnDisk(), _texts.Mode, favourites);
    }

    private async Task DownloadAsync(UsdbTextFiles.Plan plan, int version, CancellationToken ct)
    {
        int done = plan.Target - plan.Ids.Count;
        List<int> skippedInARow = [];
        foreach (int id in plan.Ids)
        {
            if (Volatile.Read(ref _version) != version || !CanRun)
            {
                return;
            }

            Report(plan.Target, done, active: true);
            try
            {
                await _usdb.DownloadTxtAsync(id, ct).ConfigureAwait(false);
                skippedInARow.Clear();
            }
            catch (UsdbException ex) when (ex.InnerException is null && _usdb.IsConnected)
            {
                // No text for this song (deleted on USDB) — the network is fine, go on with the next one.
                _log.LogInformation("USDB song {Id}: no text ({Error}) — skipped", id, ex.Message);
                _skipped.Add(id);
                plan = plan with { Target = plan.Target - 1 };
                done--;
                // Many in a row: not deleted songs but an expired session (USDB answers without the text then).
                skippedInARow.Add(id);
                if (skippedInARow.Count >= 5)
                {
                    _skipped.ExceptWith(skippedInARow);
                    await _usdb.AutoConnectAsync().ConfigureAwait(false);
                    throw new UsdbException("USDB returned no song texts for several songs in a row — logged in again");
                }
            }

            done++;
            Report(plan.Target, done, active: true);
            await WaitAsync(Gap, ct).ConfigureAwait(false);
        }

        _log.LogInformation("USDB song texts: all {Count} on this computer", plan.Target);
    }

    // A pause that a change (pause button, mode, sync) cuts short.
    private async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        int version = Volatile.Read(ref _version);
        DateTime until = DateTime.UtcNow + delay;
        while (DateTime.UtcNow < until && Volatile.Read(ref _version) == version)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
        }
    }

    private void Report(int target, int done, bool active)
    {
        if (Target == target && Done == done && IsActive == active)
        {
            return;
        }

        Target = target;
        Done = done;
        IsActive = active;
        Changed?.Invoke();
    }

    // Song ids of USDB songs are "usdb::23775".
    private static int? UsdbIdOf(string songId)
        => songId.StartsWith("usdb::", StringComparison.Ordinal) && int.TryParse(songId.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _wake.Dispose();
    }
}
