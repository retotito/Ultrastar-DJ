using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Tests.Songs;

public class SourceAvailabilityTests
{
    [Fact]
    public void Diff_ReportsWhatWentAwayAndWhatCameBack()
    {
        (IReadOnlyList<string> away, IReadOnlyList<string> back) = SourceAvailability.Diff(new HashSet<string> { "a", "b" }, new HashSet<string> { "b", "c" });

        Assert.Equal(["c"], away);
        Assert.Equal(["a"], back);
    }

    [Fact]
    public void Diff_NoChange_Nothing()
    {
        (IReadOnlyList<string> away, IReadOnlyList<string> back) = SourceAvailability.Diff(new HashSet<string> { "a" }, new HashSet<string> { "a" });

        Assert.Empty(away);
        Assert.Empty(back);
    }

    [Fact]
    public void Diff_AtStart_EverythingMissingWentAway()
    {
        (IReadOnlyList<string> away, _) = SourceAvailability.Diff(new HashSet<string>(), new HashSet<string> { "x" });

        Assert.Equal(["x"], away);
    }
}
