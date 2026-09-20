using Microsoft.Extensions.Logging;
using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.Media;

/// <summary>
/// Keeps a muted visual player aligned with the game clock (Mode A: visual position = clock + videoGap).
/// Large drift → seek; small drift → speed nudge; play/pause mirrored from the clock.
/// </summary>
public sealed class ClockFollower : IAsyncDisposable
{
    private const double SeekThresholdSec = 0.35;   // above this, nudging converges too slowly
    private const double NudgeThresholdSec = 0.05;
    private const double SettledThresholdSec = 0.02;
    private const double NudgeSpeed = 0.02;
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(50);

    private readonly IMediaPlayer _visual;
    private readonly IGameClock _clock;
    private readonly double _offsetSec;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private bool _nudging;

    public ClockFollower(IMediaPlayer visual, IGameClock clock, double offsetSec, ILogger<ClockFollower> log)
    {
        _visual = visual;
        _clock = clock;
        _offsetSec = offsetSec;
        _log = log;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Last measured drift in seconds (visual − target); for diagnostics.</summary>
    public double LastDriftSec { get; private set; }

    private async Task RunAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(Period);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                Step();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Step()
    {
        MediaState vs = _visual.State;
        if (vs is MediaState.Idle or MediaState.Loading or MediaState.Error or MediaState.Ended)
        {
            return;
        }

        bool running = _clock.IsRunning;
        if (running && vs is MediaState.Ready or MediaState.Paused)
        {
            _visual.Play();
        }
        else if (!running && vs == MediaState.Playing)
        {
            _visual.Pause();
            return;
        }

        if (!running)
        {
            return;
        }

        double target = _clock.PositionSec + _offsetSec;
        double drift = _visual.Position.TotalSeconds - target;
        LastDriftSec = drift;

        if (Math.Abs(drift) > SeekThresholdSec)
        {
            _log.LogDebug("{Player}: drift {DriftMs:F0} ms → seek", _visual.Name, drift * 1000);
            _visual.Seek(TimeSpan.FromSeconds(target));
            ResetSpeed();
        }
        else if (Math.Abs(drift) > NudgeThresholdSec)
        {
            double speed = 1.0 - Math.Sign(drift) * NudgeSpeed;
            if (!_nudging || Math.Abs(_visual.Speed - speed) > 0.001)
            {
                _visual.Speed = speed;
                _nudging = true;
            }
        }
        else if (_nudging && Math.Abs(drift) < SettledThresholdSec)
        {
            ResetSpeed();
        }
    }

    private void ResetSpeed()
    {
        if (_nudging)
        {
            _visual.Speed = 1.0;
            _nudging = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _cts.Dispose();
    }
}
