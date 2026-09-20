using UltrastarDJ.Core.Songs;
using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.Core.Game;

/// <summary>A player taking part in one song.</summary>
/// <param name="PlayerId">1–4.</param>
/// <param name="TrackIndex">Which <see cref="NoteTrack"/> the player sings (0 for solo songs; duet P2 = 1).</param>
/// <param name="MicDelayMs">Round-trip mic latency; the sample is matched against the beat this many ms earlier.</param>
public sealed record GamePlayer(int PlayerId, int TrackIndex, double MicDelayMs);

/// <summary>
/// One song's live scoring: owns a <see cref="PlayerScorer"/> per player and turns (clock position, current mic
/// notes) into <see cref="PitchTick"/>s. Pure: the caller supplies time and pitches, so it is fully testable.
/// </summary>
public sealed class GameSession
{
    private readonly Dictionary<int, (PlayerScorer Scorer, GamePlayer Player)> _players = [];

    public GameSession(Song song, IReadOnlyList<GamePlayer> players, Difficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(song.Notes);
        Song = song;
        Difficulty = difficulty;
        foreach (GamePlayer p in players)
        {
            NoteTrack track = song.Notes[Math.Clamp(p.TrackIndex, 0, song.Notes.Count - 1)];
            _players[p.PlayerId] = (new PlayerScorer(p.PlayerId, track, difficulty), p);
        }
    }

    public Song Song { get; }
    public Difficulty Difficulty { get; }
    public IEnumerable<int> PlayerIds => _players.Keys;

    public PlayerScorer Scorer(int playerId) => _players[playerId].Scorer;
    public NoteTrack TrackOf(int playerId) => Song.Notes![Math.Clamp(_players[playerId].Player.TrackIndex, 0, Song.Notes.Count - 1)];

    /// <summary>Song beat at a game time (undelayed; what the beamer draws).</summary>
    public double BeatAt(double positionSec) => BeatMath.BeatAt(positionSec, Song.Bpm, Song.GapMs);

    /// <summary>
    /// One evaluation pass. <paramref name="midiOf"/> returns the player's current smoothed MIDI note (-1 = silence).
    /// Returns one tick per player; the caller publishes them to the beamers.
    /// </summary>
    public IReadOnlyList<PitchTick> Tick(double positionSec, Func<int, double> midiOf)
    {
        double beat = BeatAt(positionSec);
        List<PitchTick> ticks = new(_players.Count);
        foreach ((PlayerScorer scorer, GamePlayer player) in _players.Values)
        {
            double evalBeat = beat - BeatMath.MsToBeats(Song.Bpm, player.MicDelayMs);
            ticks.Add(scorer.Evaluate(midiOf(player.PlayerId), evalBeat));
        }

        return ticks;
    }

    /// <summary>Final standings, highest first.</summary>
    public IReadOnlyList<(int PlayerId, int Score, int MaxScore)> Standings()
        => _players.Values.Select(p => (p.Scorer.PlayerId, p.Scorer.Score, p.Scorer.MaxScore)).OrderByDescending(x => x.Score).ToList();

    /// <summary>Last beat of the song across all tracks — playback may stop shortly after.</summary>
    public int LastBeat => Song.Notes!.Max(t => t.Lines.Count > 0 ? t.Lines[^1].EndBeat : 0);
}
