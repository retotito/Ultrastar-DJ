using System.Diagnostics;
using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.Media;

/// <summary>
/// <see cref="IGameClock"/> driven by the audio player's file position. mpv reports <c>time-pos</c>
/// only a few times per second, so between reports the clock interpolates with a stopwatch, clamped
/// so it can never run ahead of the next plausible report.
/// </summary>
public sealed class MediaGameClock : IGameClock
{
    // mpv updates time-pos at least every video frame or ~50 ms for audio; never extrapolate further than this.
    private const double MaxExtrapolationSec = 0.10;

    private readonly Func<double> _readPositionSec;
    private readonly Func<bool> _readPlaying;
    private readonly Func<double> _readSpeed;
    private readonly Stopwatch _sinceReport = new();
    private double _lastReportSec = double.NaN;
    private double _originSec;

    public MediaGameClock(IMediaPlayer audio, double originSec = 0)
        : this(() => audio.Position.TotalSeconds, () => audio.State == MediaState.Playing, () => audio.Speed, originSec)
    {
    }

    /// <summary>Test seam: pure function inputs instead of a player.</summary>
    public MediaGameClock(Func<double> readPositionSec, Func<bool> readPlaying, Func<double> readSpeed, double originSec = 0)
    {
        _readPositionSec = readPositionSec;
        _readPlaying = readPlaying;
        _readSpeed = readSpeed;
        _originSec = originSec;
    }

    /// <summary>File position that corresponds to game time 0 (<see cref="MediaPlan.AudioOriginSec"/>).</summary>
    public double OriginSec
    {
        get => _originSec;
        set => _originSec = value;
    }

    public bool IsRunning => _readPlaying();

    public double PositionSec
    {
        get
        {
            double reported = _readPositionSec();
            if (reported != _lastReportSec)
            {
                _lastReportSec = reported;
                _sinceReport.Restart();
            }

            double extra = 0;
            if (_readPlaying())
            {
                extra = Math.Min(MaxExtrapolationSec, _sinceReport.Elapsed.TotalSeconds * _readSpeed());
            }

            return Math.Max(0, reported + extra - _originSec);
        }
    }
}
