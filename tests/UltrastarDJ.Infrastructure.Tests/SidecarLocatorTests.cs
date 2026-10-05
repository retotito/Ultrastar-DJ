namespace UltrastarDJ.Infrastructure.Tests;

public sealed class SidecarLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "usdj-tests", Guid.NewGuid().ToString("N"));

    public SidecarLocatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private string Touch(params string[] parts)
    {
        string path = Path.Combine([_dir, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Find_FolderBuild_IsFound()
    {
        string expected = Touch("yt-dlp", Exe("yt-dlp"));

        Assert.Equal(expected, new SidecarLocator([_dir]).Find("yt-dlp"));
    }

    [Fact]
    public void Find_FolderWithoutExecutable_FallsThroughToNextDir()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a", "yt-dlp", "_internal"));
        string expected = Touch("b", "yt-dlp", Exe("yt-dlp"));

        Assert.Equal(expected, new SidecarLocator([Path.Combine(_dir, "a"), Path.Combine(_dir, "b")]).Find("yt-dlp"));
    }

    [Fact]
    public void Find_SingleFile_IsFound()
    {
        string expected = Touch(Exe("ffmpeg"));

        Assert.Equal(expected, new SidecarLocator([_dir]).Find("ffmpeg"));
    }
}
