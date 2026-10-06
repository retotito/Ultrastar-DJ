using UltrastarDJ.Core.Timing;
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
    /// <summary>Guards <see cref="Lanes"/> and <see cref="Ratings"/> between the tick thread and the renderer.</summary>
    public Lock Sync { get; } = new();

    public GameScene(GameSession session, IReadOnlyList<ScenePlayer> players, Func<double> positionSec, SongTimeline? timeline, Func<double> clockSec,
        Func<int, double> micActivity)
    {
        MicActivity = micActivity;
        Session = session;
        Players = players;
        PositionSec = positionSec;
        Timeline = timeline;
        ClockSec = clockSec;
        foreach (ScenePlayer p in players)
        {
            Lanes[p.Id] = new LaneState(session.TrackOf(p.Id));
        }
    }

    public GameSession Session { get; }
    public IReadOnlyList<ScenePlayer> Players { get; }
    public Func<double> PositionSec { get; }
    public SongTimeline? Timeline { get; }
    /// <summary>Media clock without the lyrics offset — elapsed / remaining are real playing time.</summary>
    public Func<double> ClockSec { get; }
    public Dictionary<int, LaneState> Lanes { get; } = [];
    /// <summary>Player id → how loud they sing now, 0..1 above the gate (the mic meter next to the name).</summary>
    public Func<int, double> MicActivity { get; }

    /// <summary>The latest phrase rating per player (popup and, for a 100 % phrase, the star burst).</summary>
    public Dictionary<int, PhraseResult> Ratings { get; } = [];

    /// <summary>Tick thread: records results and rates every phrase the player's sung beat has passed.</summary>
    public void Apply(IReadOnlyList<PitchTick> ticks, double positionSec)
    {
        lock (Sync)
        {
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

                RatePassedLines(t.PlayerId, lane, positionSec);
            }
        }
    }

    // Rated once the mic has delivered the phrase's last beat (sung beat = display − mic delay), as USDX's
    // OnSentenceEnd. Phrases already behind at the start (a later #START) are skipped without a rating.
    private void RatePassedLines(int playerId, LaneState lane, double positionSec)
    {
        double sungBeat = Session.SungBeatAt(positionSec, playerId);
        IReadOnlyList<LyricLine> lines = lane.Track.Lines;
        PlayerScorer scorer = Session.Scorer(playerId);
        while (lane.NextToRate < lines.Count && lines[lane.NextToRate].EndBeat <= sungBeat)
        {
            LyricLine line = lines[lane.NextToRate++];
            if (!lane.Started)
            {
                continue;
            }

            int rating = PhraseRating.Rate(scorer.LinePerfection(line));
            bool full = scorer.IsLinePerfect(line);
            if (PhraseRating.Text(rating) is not null || full)
            {
                Ratings[playerId] = new PhraseResult(positionSec, rating, full);
            }
        }

        lane.Started = true;
    }
}

public readonly record struct BeatResult(bool Correct, double RowPitch);

/// <param name="AtSec">Display time the phrase was rated.</param>
/// <param name="Rating">0..8 (<see cref="PhraseRating"/>).</param>
/// <param name="Full">Every beat correct — the star burst.</param>
public readonly record struct PhraseResult(double AtSec, int Rating, bool Full);

/// <summary>Per-player sung history for the current song. Keyed by integer beat; last tick per beat wins.</summary>
public sealed class LaneState(NoteTrack track)
{
    public NoteTrack Track { get; } = track;
    public Dictionary<int, BeatResult> Results { get; } = [];
    /// <summary>Index of the next phrase to rate. Tick thread, under <c>Sync</c>.</summary>
    public int NextToRate { get; set; }

    /// <summary>False until the first tick: phrases before the start position are skipped, not rated.</summary>
    public bool Started { get; set; }

    /// <summary>Game time a note (by start beat) first reached ≥ 50 % correct — starts its pulse. Render thread, under <c>Sync</c>.</summary>
    public Dictionary<int, double> CorrectSince { get; } = [];

    /// <summary>Game time a rap note (by start beat) was first hit — starts its glow. Render thread, under <c>Sync</c>.</summary>
    public Dictionary<int, double> RapHitAt { get; } = [];
}
