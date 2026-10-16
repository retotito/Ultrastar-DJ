namespace UltrastarDJ.Core.Players;

/// <summary>Which channel of an input device a player sings into.</summary>
public enum MicChannelSide
{
    Left,
    Right,
    Mono,
}

public sealed record MicBinding(string DeviceId, MicChannelSide Channel)
{
    /// <summary>
    /// Two players cannot share an input: same device and same side, or either one on Mono (which uses both sides).
    /// Left and Right of one dongle are two separate singers.
    /// </summary>
    public bool ConflictsWith(MicBinding other)
        => DeviceId == other.DeviceId && (Channel == other.Channel || Channel == MicChannelSide.Mono || other.Channel == MicChannelSide.Mono);
}

/// <summary>One of the four fixed player slots. Persisted as a settings document.</summary>
public sealed record PlayerConfig
{
    public const int MaxPlayers = 4;
    public static readonly IReadOnlyList<string> Colors = ["blue", "red", "green", "yellow"];

    public required int Id { get; init; }
    public bool Active { get; init; }
    /// <summary>"Player 1" … in the UI language — fixed per slot, not stored.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Name => Localization.Text.F("player.name", "Player {0}", Id);
    /// <summary>Colour key (blue/red/green/yellow); the UI maps it to <c>ColorPlayerN</c>.</summary>
    public required string Color { get; init; }
    public MicBinding? Mic { get; init; }
    /// <summary>Input gain 0.01–10 (the UI edits it in dB, <see cref="InputGainScale"/>), applied before the gate.</summary>
    public double InputGain { get; init; } = 1.0;
    /// <summary>Noise gate on block RMS (linear, post-gain). Default 0.003 ≈ −50 dBFS; the UI edits it in dB.</summary>
    public double Threshold { get; init; } = 0.003;
    /// <summary>Level of this mic in the speaker mix, 0–1 (older settings may hold up to 2; read as 1).</summary>
    public double MixGain { get; init; } = 1.0;
    /// <summary>Mic removed from the speaker mix during a song (Game Player mute; scoring unaffected).</summary>
    public bool MixMuted { get; init; }

    /// <summary>The Game Player's mute is for the song only: in the Audio Input mic test the mic is always heard.</summary>
    public bool MutedInMonitor(bool duringSong) => duringSong && MixMuted;

    /// <summary>
    /// Calibrate's results per game output (OutputsService key): ms from the tone leaving the app until the pitch
    /// analysis recognised it. Speakers differ by hundreds of ms (cable vs Bluetooth), so each keeps its own; on an
    /// output without one the mic delay is an estimate (Core.Timing.LatencyModel.MicDelay).
    /// </summary>
    public IReadOnlyDictionary<string, double> Calibrations { get; init; } = new Dictionary<string, double>();

    /// <summary>Settings before per-output calibration: the one result. Read by <see cref="Migrated"/>, never written again.</summary>
    public double? CalibratedTotalMs { get; init; }

    /// <summary>Settings before per-output calibration: the output it was measured on. See <see cref="CalibratedTotalMs"/>.</summary>
    public string? CalibrationOutputKey { get; init; }

    public double? CalibrationFor(string outputKey) => Calibrations.TryGetValue(outputKey, out double ms) ? ms : null;

    public PlayerConfig WithCalibration(string outputKey, double totalMs)
        => this with { Calibrations = new Dictionary<string, double>(Calibrations) { [outputKey] = totalMs } };

    /// <summary>Moves an old single calibration to its output.</summary>
    public PlayerConfig Migrated()
    {
        PlayerConfig p = CalibratedTotalMs is { } total && CalibrationOutputKey is { } key && !Calibrations.ContainsKey(key)
            ? WithCalibration(key, total)
            : this;
        return p with { CalibratedTotalMs = null, CalibrationOutputKey = null };
    }

    public static PlayerConfig Default(int id) => new()
    {
        Id = id,
        Active = id <= 2,
        Color = Colors[(id - 1) % Colors.Count],
    };
}
