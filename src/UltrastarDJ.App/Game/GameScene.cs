using Avalonia.Media;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.App.Game;

/// <summary>A player as the beamer draws them.</summary>
public sealed record ScenePlayer(int Id, string Name, IBrush Brush, Color Color);

/// <summary>
/// Everything the beamer overlay needs, shared between the view model (writer, tick thread) and the
/// <see cref="Controls.GameOverlayControl"/> (reader, UI thread). Guarded by <see cref="Sync"/>.
/// </summary>
public sealed class GameScene
{
    /// <summary>Guards <see cref="Lanes"/> and <see cref="PerfectFlashAt"/> between the tick thread and the renderer.</summary>
    public Lock Sync { get; } = new();

    public GameScene(GameSession session, IReadOnlyList<ScenePlayer> players, Func<double> positionSec)
    {
        Session = session;
        Players = players;
        PositionSec = positionSec;
        foreach (ScenePlayer p in players)
        {
            Lanes[p.Id] = new LaneState(session.TrackOf(p.Id));
        }
    }

    public GameSession Session { get; }
    public IReadOnlyList<ScenePlayer> Players { get; }
    public Func<double> PositionSec { get; }
    public Dictionary<int, LaneState> Lanes { get; } = [];

    /// <summary>Game-time when a "PERFECT" flash started, per player (drawn for ~1.2 s).</summary>
    public Dictionary<int, double> PerfectFlashAt { get; } = [];

    /// <summary>Tick thread: records results and detects phrase completion.</summary>
    public void Apply(IReadOnlyList<PitchTick> ticks, double positionSec)
    {
        lock (Sync)
        {
            double beat = Session.BeatAt(positionSec);
            foreach (PitchTick t in ticks)
            {
                if (!Lanes.TryGetValue(t.PlayerId, out LaneState? lane))
                {
                    continue;
                }

                if (t.Beat >= 0)
                {
                    lane.Results[t.Beat] = new BeatResult(t.Correct, t.RowPitch);
                }

                // Phrase switch: award the flash if every scorable beat of the line that just ended was correct.
                LyricLine? active = NoteLaneGeometry.ActiveLine(lane.Track, beat);
                if (!ReferenceEquals(active, lane.LastActiveLine))
                {
                    if (lane.LastActiveLine is { } prev && Session.Scorer(t.PlayerId).IsLinePerfect(prev))
                    {
                        PerfectFlashAt[t.PlayerId] = positionSec;
                    }

                    lane.LastActiveLine = active;
                }
            }
        }
    }
}

public readonly record struct BeatResult(bool Correct, double RowPitch);

/// <summary>Per-player sung history for the current song. Keyed by integer beat; last tick per beat wins.</summary>
public sealed class LaneState(NoteTrack track)
{
    public NoteTrack Track { get; } = track;
    public Dictionary<int, BeatResult> Results { get; } = [];
    public LyricLine? LastActiveLine { get; set; }
}
