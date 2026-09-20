using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SongValidatorTests
{
    private sealed class FakeFiles(params string[] existing) : IFileExistence
    {
        public string? Text { get; set; } = ": 0 1 0 la\n";
        public bool Exists(string path) => existing.Contains(path);
        public string? ReadText(string path) => Text;
    }

    private static Song Song(string? audio = null, string? video = null, string? yt = null) => new()
    {
        Id = "s::1", SourceId = "s", Title = "T", Artist = "A", Bpm = 120, TxtPath = "/s/a.txt",
        AudioPath = audio, VideoPath = video, YouTubeId = yt,
    };

    [Fact]
    public void Valid_WhenAudioExists()
    {
        SongValidationResult r = new SongValidator(new FakeFiles("/s/a.mp3")).Validate(Song(audio: "/s/a.mp3"));

        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void MissingAudio_WithVideoFallback_PatchesAudioOut()
    {
        SongValidationResult r = new SongValidator(new FakeFiles("/s/v.mp4")).Validate(Song(audio: "/s/a.mp3", video: "/s/v.mp4"));

        Assert.True(r.IsValid);
        Assert.Null(r.Song.AudioPath);
        Assert.Equal("/s/v.mp4", r.Song.VideoPath);
    }

    [Fact]
    public void MissingAudio_NoFallback_IsError()
    {
        SongValidationResult r = new SongValidator(new FakeFiles()).Validate(Song(audio: "/s/a.mp3"));

        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Field == "audioPath");
    }

    [Fact]
    public void MissingVideo_WithAudio_PatchesVideoOut()
    {
        SongValidationResult r = new SongValidator(new FakeFiles("/s/a.mp3")).Validate(Song(audio: "/s/a.mp3", video: "/s/v.mp4"));

        Assert.True(r.IsValid);
        Assert.Null(r.Song.VideoPath);
    }

    [Fact]
    public void MissingVideo_AsSoleSource_IsError()
    {
        SongValidationResult r = new SongValidator(new FakeFiles()).Validate(Song(video: "/s/v.mp4"));

        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Field == "videoPath");
    }

    [Fact]
    public void YouTubeOnly_NeedsNoFiles()
    {
        Assert.True(new SongValidator(new FakeFiles()).Validate(Song(yt: "dQw4w9WgXcQ")).IsValid);
    }

    [Fact]
    public void NoNoteLines_IsError()
    {
        FakeFiles files = new("/s/a.mp3") { Text = "#TITLE:x\n" };

        SongValidationResult r = new SongValidator(files).Validate(Song(audio: "/s/a.mp3"));

        Assert.Contains(r.Errors, e => e.Field == "notes");
    }

    [Fact]
    public void InvalidBpm_IsError()
    {
        SongValidationResult r = new SongValidator(new FakeFiles("/s/a.mp3")).Validate(Song(audio: "/s/a.mp3") with { Bpm = 0 });

        Assert.Contains(r.Errors, e => e.Field == "bpm");
    }

    [Fact]
    public void MissingCover_IsPatchedNotAnError()
    {
        SongValidationResult r = new SongValidator(new FakeFiles("/s/a.mp3")).Validate(Song(audio: "/s/a.mp3") with { CoverPath = "/s/co.jpg" });

        Assert.True(r.IsValid);
        Assert.Null(r.Song.CoverPath);
    }
}
