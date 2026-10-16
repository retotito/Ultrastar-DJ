namespace UltrastarDJ.Core.Timing;

/// <summary>What kind of speaker an output is — only to estimate its latency while nobody measured it.</summary>
public enum OutputKind
{
    /// <summary>Built-in speakers, a cable, USB.</summary>
    Other,
    Bluetooth,
    /// <summary>A TV or monitor over HDMI / DisplayPort.</summary>
    Display,
}
