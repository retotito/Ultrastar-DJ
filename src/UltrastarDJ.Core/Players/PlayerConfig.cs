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
    public required string Name { get; init; }
    /// <summary>Colour key (blue/red/green/yellow); the UI maps it to <c>ColorPlayerN</c>.</summary>
    public required string Color { get; init; }
    public MicBinding? Mic { get; init; }
    /// <summary>Input gain 0–10 applied before the gate (quiet USB mics need 4–8).</summary>
    public double InputGain { get; init; } = 1.0;
    /// <summary>Noise gate on block RMS (linear, post-gain). Default 0.003 ≈ −50 dBFS; the UI edits it in dB.</summary>
    public double Threshold { get; init; } = 0.003;
    /// <summary>Level of this mic in the speaker mix, 0–2.</summary>
    public double MixGain { get; init; } = 1.0;
    /// <summary>Mic removed from the speaker mix (scoring unaffected).</summary>
    public bool MixMuted { get; init; }
    /// <summary>
    /// Calibrate's result: a tone on the game output until the pitch analysis recognised it (ms); null = never
    /// calibrated. The scoring delay is this minus the latency of <see cref="CalibrationOutputKey"/>
    /// (Core.Timing.LatencyModel).
    /// </summary>
    public double? CalibratedTotalMs { get; init; }

    /// <summary>The output (OutputsService key) the calibration tone played on.</summary>
    public string? CalibrationOutputKey { get; init; }

    public static PlayerConfig Default(int id) => new()
    {
        Id = id,
        Active = id <= 2,
        Name = $"Player {id}",
        Color = Colors[(id - 1) % Colors.Count],
    };
}
