using UltrastarDJ.Core.Updates;

namespace UltrastarDJ.Core.Tests.Updates;

public class ReleaseNotesTests
{
    [Fact]
    public void Bullets_BecomePlainLines()
    {
        const string md = "## What's new\n\n- **Queue** kept over a restart\r\n* Fewer `YouTube` lookups\n\nPlain sentence.\n";

        Assert.Equal(["Queue kept over a restart", "Fewer YouTube lookups", "Plain sentence."], ReleaseNotes.Lines(md));
    }

    [Fact]
    public void NoNotes_NoLines()
    {
        Assert.Empty(ReleaseNotes.Lines(null));
        Assert.Empty(ReleaseNotes.Lines("\n  \n# Title\n- \nhttps://github.com/x/releases"));
    }
}
