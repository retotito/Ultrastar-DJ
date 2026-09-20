namespace UltrastarDJ.Media;

/// <summary>What a player should open. Local file or a YouTube video id (resolved by mpv's ytdl hook).</summary>
public abstract record MediaSource
{
    public sealed record LocalFile(string Path) : MediaSource
    {
        public override string ToMpvUri() => Path;
        public override string ToString() => System.IO.Path.GetFileName(Path);
    }

    public sealed record YouTube(string VideoId) : MediaSource
    {
        public override string ToMpvUri() => $"https://www.youtube.com/watch?v={VideoId}";
        public override string ToString() => $"youtube:{VideoId}";
    }

    /// <summary>The string mpv's <c>loadfile</c> accepts for this source.</summary>
    public abstract string ToMpvUri();
}

/// <summary>Per-load options. Which tracks to decode and where to start/stop inside the file.</summary>
public sealed record MediaLoadOptions
{
    public bool Audio { get; init; } = true;
    public bool Video { get; init; } = true;
    /// <summary>Start position inside the file (Mode B <c>#VIDEOGAP</c> or <c>#START</c>).</summary>
    public TimeSpan StartAt { get; init; } = TimeSpan.Zero;
    /// <summary>Stop position inside the file (<c>#END</c>), null = play to the end.</summary>
    public TimeSpan? EndAt { get; init; }
    /// <summary>Cap for YouTube/large sources. 720 is enough for projectors; preview uses 360.</summary>
    public int MaxHeight { get; init; } = 720;
    /// <summary>Whether to keep playing after load (false = load paused, ready to <see cref="IMediaPlayer.Play"/>).</summary>
    public bool Autoplay { get; init; }
}

public enum MediaState
{
    Idle,
    Loading,
    Buffering,
    Ready,
    Playing,
    Paused,
    Ended,
    Error,
}

/// <summary>An audio output as mpv names it. <see cref="Id"/> is what goes into <c>audio-device</c>.</summary>
public sealed record AudioOutputDevice(string Id, string Name)
{
    public static AudioOutputDevice Auto { get; } = new("auto", "System default");
}
