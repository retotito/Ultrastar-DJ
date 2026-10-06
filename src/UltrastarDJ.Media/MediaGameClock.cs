using System.Diagnostics;
using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.Media;

/// <summary>
/// <see cref="IGameClock"/> for the game: a smooth, never-backwards song time, synchronised to the audio player.
/// After UltraStar Deluxe's <c>TLyricsState.Synchronize</c>: an own high-resolution timer drives the time; mpv's
/// <c>time-pos</c> (a few reports per second, each a little off) only corrects it — the difference is averaged,
/// a timer behind the audio jumps forward, a timer ahead of it holds until the audio catches up. Seeks and big
/// jumps are followed at once; while paused the clock is exactly the audio position.
/// Everything smooth on the beamer (note fill, lyrics sweep, lead-in, times) reads this.
/// </summary>
public sealed class MediaGameClock : IGameClock
{
    // Weight of the previous average in the running difference (USDX: AVG_HISTORY_FACTOR).
    private const double AvgHistory = 0.7;
    // Below this the timer is considered in sync (USDX: 10 ms).
    private const double SyncThresholdSec = 0.010;
    // Further off than this is a seek / restart, not drift: follow the audio at once.
    private const double JumpSec = 0.25;

    private static readonly double TicksToSec = 1.0 / Stopwatch.Frequency;

    private readonly Func<double> _readPositionSec;
    private readonly Func<bool> _readPlaying;
    private readonly Func<double> _readSpeed;
    private readonly Func<double> _nowSec;
    private readonly Lock _gate = new();
    private double _originSec;
    private double _time = double.NaN;
    private double _lastNow;
    private double _lastReported = double.NaN;
    private double _avgDiff = double.NaN;
    private bool _held;

    public MediaGameClock(IMediaPlayer audio, double originSec = 0)
        : this(() => audio.Position.TotalSeconds, () => audio.State == MediaState.Playing, () => audio.Speed, originSec,
            () => Stopwatch.GetTimestamp() * TicksToSec)
    {
    }

    /// <summary>Test seam: pure function inputs instead of a player and the wall clock.</summary>
    public MediaGameClock(Func<double> readPositionSec, Func<bool> readPlaying, Func<double> readSpeed, double originSec = 0, Func<double>? nowSec = null)
    {
        _readPositionSec = readPositionSec;
        _readPlaying = readPlaying;
        _readSpeed = readSpeed;
        _originSec = originSec;
        _nowSec = nowSec ?? (() => Stopwatch.GetTimestamp() * TicksToSec);
    }

    /// <summary>File position that corresponds to game time 0 (<see cref="MediaPlan.AudioOriginSec"/>).</summary>
    public double OriginSec
    {
        get => _originSec;
        set => _originSec = value;
    }

    public bool IsRunning => _readPlaying();

    /// <summary>Thread-safe: read by the game ticker and the beamers' render loop.</summary>
    public double PositionSec
    {
        get
        {
            lock (_gate)
            {
                return Math.Max(0, Advance() - _originSec);
            }
        }
    }

    private double Advance()
    {
        double now = _nowSec();
        double reported = _readPositionSec();
        if (double.IsNaN(_time) || !_readPlaying())
        {
            Reset(reported, now);
            return _time;
        }

        if (!_held)
        {
            _time += (now - _lastNow) * _readSpeed();
        }

        _lastNow = now;
        if (reported != _lastReported)
        {
            _lastReported = reported;
            Synchronize(reported, now);
        }

        return _time;
    }

    private void Synchronize(double reported, double now)
    {
        double diff = reported - _time;
        if (Math.Abs(diff) > JumpSec)
        {
            Reset(reported, now);
            return;
        }

        _avgDiff = double.IsNaN(_avgDiff) ? diff : diff * (1 - AvgHistory) + _avgDiff * AvgHistory;
        if (_avgDiff > SyncThresholdSec)
        {
            // Timer behind the audio: catch up (forward only — scoring never sees time run back).
            _time += _avgDiff;
            _avgDiff = double.NaN;
            _held = false;
        }
        else if (_avgDiff < -SyncThresholdSec)
        {
            // Timer ahead: stand still until the audio has caught up.
            _held = true;
        }
        else if (_held && diff >= 0)
        {
            _held = false;
        }
    }

    private void Reset(double reported, double now)
    {
        _time = reported;
        _lastReported = reported;
        _lastNow = now;
        _avgDiff = double.NaN;
        _held = false;
    }
}
