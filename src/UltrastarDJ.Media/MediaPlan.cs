namespace UltrastarDJ.Media;

/// <summary>
/// The media-relevant part of a song. <c>Core.Song</c> maps onto this so the resolver has no
/// dependency on the full domain model.
/// </summary>
public sealed record SongMedia
{
    public string? AudioPath { get; init; }
    public string? VideoPath { get; init; }
    public string? YouTubeId { get; init; }
    public string? BackgroundPath { get; init; }
    public string? CoverPath { get; init; }
    /// <summary><c>#VIDEOGAP</c>: where in the video the song content begins.</summary>
    public double VideoGapSec { get; init; }
    /// <summary><c>#START</c>, seconds into the audio.</summary>
    public double? StartSec { get; init; }
    /// <summary><c>#END</c>, seconds into the audio.</summary>
    public double? EndSec { get; init; }
}

/// <summary>How <c>#VIDEOGAP</c> applies. See docs/02-media-engine.md.</summary>
public enum VideoGapMode
{
    /// <summary>No video → gap irrelevant.</summary>
    None,
    /// <summary>Separate audio file: visual must sit at <c>T + videoGap</c>.</summary>
    OffsetVisual,
    /// <summary>Video is the audio source: both start at <c>videoGap</c>; game time = position − videoGap.</summary>
    SkipIntro,
}

public enum StaticBackground
{
    None,
    Image,
    Cover,
}

/// <summary>One player's job inside a plan.</summary>
public sealed record MediaRole(MediaSource Source, MediaLoadOptions Options);

/// <summary>
/// What each player in a channel plays for a song. Pure data; produced by <see cref="MediaSourceResolver"/>.
/// </summary>
public sealed record MediaPlan
{
    /// <summary>Audio authority. May also carry the video (cases 4 and 6).</summary>
    public required MediaRole Audio { get; init; }
    /// <summary>Separate muted visual player (cases 2 and 3); null when the audio role shows the video or there is none.</summary>
    public MediaRole? Visual { get; init; }
    public VideoGapMode GapMode { get; init; }
    public double VideoGapSec { get; init; }
    public StaticBackground Background { get; init; }
    public string? BackgroundPath { get; init; }
    /// <summary>Which of the six documented cases this is, for logs and tests.</summary>
    public int Case { get; init; }

    public bool AudioRoleHasVideo => Audio.Options.Video;
    /// <summary>Game time 0 corresponds to this position inside the audio player's file.</summary>
    public double AudioOriginSec => GapMode == VideoGapMode.SkipIntro ? VideoGapSec : 0;
}

/// <summary>Maps a song's media files onto players. Pure function — unit-tested for all six cases.</summary>
public static class MediaSourceResolver
{
    public static MediaPlan Resolve(SongMedia s, int maxHeight = 720)
    {
        bool hasAudio = !string.IsNullOrEmpty(s.AudioPath);
        bool hasVideo = !string.IsNullOrEmpty(s.VideoPath);
        bool hasYt = !string.IsNullOrEmpty(s.YouTubeId);
        double gap = Math.Max(0, s.VideoGapSec);
        TimeSpan? end = s.EndSec is { } e ? TimeSpan.FromSeconds(e) : null;
        TimeSpan start = TimeSpan.FromSeconds(s.StartSec ?? 0);

        if (hasAudio)
        {
            MediaRole audio = new(new MediaSource.LocalFile(s.AudioPath!), new MediaLoadOptions { Video = false, StartAt = start, EndAt = end });

            if (hasVideo)
            {
                return new MediaPlan
                {
                    Case = 2,
                    Audio = audio,
                    Visual = new MediaRole(new MediaSource.LocalFile(s.VideoPath!), new MediaLoadOptions { Audio = false, MaxHeight = maxHeight, StartAt = start + TimeSpan.FromSeconds(gap) }),
                    GapMode = VideoGapMode.OffsetVisual,
                    VideoGapSec = gap,
                };
            }

            if (hasYt)
            {
                return new MediaPlan
                {
                    Case = 3,
                    Audio = audio,
                    Visual = new MediaRole(new MediaSource.YouTube(s.YouTubeId!), new MediaLoadOptions { Audio = false, MaxHeight = maxHeight, StartAt = start + TimeSpan.FromSeconds(gap) }),
                    GapMode = VideoGapMode.OffsetVisual,
                    VideoGapSec = gap,
                };
            }

            return !string.IsNullOrEmpty(s.BackgroundPath)
                ? new MediaPlan { Case = 1, Audio = audio, Background = StaticBackground.Image, BackgroundPath = s.BackgroundPath }
                : new MediaPlan { Case = 5, Audio = audio, Background = StaticBackground.Cover, BackgroundPath = s.CoverPath };
        }

        // Mode B: the video file/stream is the audio. Skip the intro for both.
        TimeSpan skipStart = start + TimeSpan.FromSeconds(gap);
        TimeSpan? skipEnd = end is { } en ? en + TimeSpan.FromSeconds(gap) : null;

        if (hasYt)
        {
            return new MediaPlan
            {
                Case = 4,
                Audio = new MediaRole(new MediaSource.YouTube(s.YouTubeId!), new MediaLoadOptions { MaxHeight = maxHeight, StartAt = skipStart, EndAt = skipEnd }),
                GapMode = VideoGapMode.SkipIntro,
                VideoGapSec = gap,
            };
        }

        if (hasVideo)
        {
            return new MediaPlan
            {
                Case = 6,
                Audio = new MediaRole(new MediaSource.LocalFile(s.VideoPath!), new MediaLoadOptions { MaxHeight = maxHeight, StartAt = skipStart, EndAt = skipEnd }),
                GapMode = VideoGapMode.SkipIntro,
                VideoGapSec = gap,
            };
        }

        throw new MediaException("Song has no playable media (no #MP3, #VIDEO or YouTube id).");
    }
}
