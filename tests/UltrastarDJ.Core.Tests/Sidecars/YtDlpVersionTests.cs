using UltrastarDJ.Core.Sidecars;

namespace UltrastarDJ.Core.Tests.Sidecars;

public class YtDlpVersionTests
{
    [Theory]
    [InlineData("2026.10.05", "2026.08.19", true)]
    [InlineData("2026.08.19.1", "2026.08.19", true)]    // hotfix
    [InlineData("2026.08.19", "2026.08.19", false)]
    [InlineData("2026.08.19", "2026.10.05", false)]
    [InlineData("2027.01.02", "2026.12.30", true)]
    [InlineData(null, "2026.08.19", false)]
    [InlineData("nightly", "2026.08.19", false)]
    public void IsNewer_ComparesDateVersions(string? latest, string? current, bool newer)
    {
        Assert.Equal(newer, YtDlpVersion.IsNewer(latest, current));
    }

    [Fact]
    public void AgeInDays_FromTheVersionDate()
    {
        Assert.Equal(49, YtDlpVersion.AgeInDays("2026.08.19", new DateOnly(2026, 10, 7)));
        Assert.Null(YtDlpVersion.AgeInDays("garbage", new DateOnly(2026, 10, 7)));
    }
}
