using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.Core.Tests.Displays;

public class WindowPlacementTests
{
    // A laptop (Retina, scaling 2) and an external monitor to its right.
    private static readonly ScreenArea Laptop = new(0, 0, 3420, 2140, 2);
    private static readonly ScreenArea Monitor = new(3420, 0, 1920, 1080, 1);

    private static (int, int, double, double, WindowMode) Where(WindowPlacement p) => (p.X, p.Y, p.Width, p.Height, p.Mode);

    [Fact]
    public void OnAConnectedScreen_IsKept()
    {
        WindowPlacement saved = new(100, 80, 1400, 860, WindowMode.Normal);
        Assert.Equal(Where(saved), Where(WindowPlacement.Fit(saved, [Laptop, Monitor], 1100, 700)!));
    }

    [Fact]
    public void OnTheExternalMonitor_IsKept()
    {
        WindowPlacement saved = new(3500, 50, 1200, 800, WindowMode.Maximized);
        Assert.Equal(Where(saved), Where(WindowPlacement.Fit(saved, [Laptop, Monitor], 1100, 700)!));
    }

    [Fact]
    public void ScreenGone_ReturnsNull_SoTheWindowIsCentred()
    {
        // Saved on the external monitor, which is not connected today.
        WindowPlacement saved = new(3500, 50, 1200, 800, WindowMode.Normal);
        Assert.Null(WindowPlacement.Fit(saved, [Laptop], 1100, 700));
    }

    [Fact]
    public void TooBigForItsScreen_IsShrunkAndMovedIn()
    {
        // 1920×1080 px at scaling 1 = 1920×1080 DIPs; a 2400×1400 window does not fit.
        WindowPlacement saved = new(3500, 100, 2400, 1400, WindowMode.Normal);
        WindowPlacement fitted = WindowPlacement.Fit(saved, [Laptop, Monitor], 1100, 700)!;

        Assert.Equal((1920.0, 1080.0), (fitted.Width, fitted.Height));
        Assert.Equal((3420, 0), (fitted.X, fitted.Y));
    }

    [Fact]
    public void HangingOffTheRightEdge_IsMovedIn()
    {
        // Laptop: 1710×1070 DIPs. Window 1400 wide at x = 1000 px (500 DIPs) ends past the edge.
        WindowPlacement fitted = WindowPlacement.Fit(new(1000, 100, 1400, 860, WindowMode.Normal), [Laptop], 1100, 700)!;
        Assert.Equal(3420 - 1400 * 2, fitted.X);
    }

    [Fact]
    public void NeverSmallerThanTheMinimum()
    {
        WindowPlacement fitted = WindowPlacement.Fit(new(100, 100, 300, 200, WindowMode.Normal), [Laptop], 1100, 700)!;
        Assert.Equal((1100.0, 700.0), (fitted.Width, fitted.Height));
    }

    // ── Display windows: found again by the screen's name ──

    private static readonly ScreenArea Projector = new(3420, 0, 1920, 1080, 1, "Epson Projector");

    [Fact]
    public void ScreenFoundByName_AfterTheArrangementChanged()
    {
        // Saved with the projector right of the laptop; today it is arranged left of it (x = -1920).
        WindowPlacement saved = new(3420 + 100, 50, 1200, 700, WindowMode.FullScreen, "Epson Projector", 3420, 0);
        ScreenArea movedProjector = Projector with { X = -1920 };

        WindowPlacement fitted = WindowPlacement.Fit(saved, [Laptop, movedProjector], 400, 300)!;

        Assert.Equal((-1920 + 100, 50), (fitted.X, fitted.Y));
        Assert.Equal(WindowMode.FullScreen, fitted.Mode);
    }

    [Fact]
    public void NamedScreenGone_ButAnotherScreenAtThatSpot_IsUsed()
    {
        // A different monitor plugged in where the projector was.
        WindowPlacement saved = new(3420 + 100, 50, 1200, 700, WindowMode.Maximized, "Epson Projector", 3420, 0);
        ScreenArea otherTv = Projector with { Name = "LG TV" };

        Assert.NotNull(WindowPlacement.Fit(saved, [Laptop, otherTv], 400, 300));
    }

    [Fact]
    public void NamedScreenGone_NothingThere_ReturnsNull()
    {
        WindowPlacement saved = new(3420 + 100, 50, 1200, 700, WindowMode.FullScreen, "Epson Projector", 3420, 0);
        Assert.Null(WindowPlacement.Fit(saved, [Laptop], 400, 300));
    }

    [Fact]
    public void TwoScreensWithTheSameName_ThePositionDecides()
    {
        // Two identical TVs: the one at the saved position wins.
        ScreenArea tvA = new(3420, 0, 1920, 1080, 1, "LG TV");
        ScreenArea tvB = new(5340, 0, 1920, 1080, 1, "LG TV");
        WindowPlacement saved = new(5340 + 10, 10, 1000, 600, WindowMode.Normal, "LG TV", 5340, 0);

        Assert.Equal(5350, WindowPlacement.Fit(saved, [Laptop, tvA, tvB], 400, 300)!.X);
    }
}
