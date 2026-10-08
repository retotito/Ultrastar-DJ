using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.Core.Tests.Displays;

public class WindowPlacementTests
{
    // A laptop (Retina, scaling 2) and an external monitor to its right.
    private static readonly ScreenArea Laptop = new(0, 0, 3420, 2140, 2);
    private static readonly ScreenArea Monitor = new(3420, 0, 1920, 1080, 1);

    [Fact]
    public void OnAConnectedScreen_IsKept()
    {
        WindowPlacement saved = new(100, 80, 1400, 860, WindowMode.Normal);
        Assert.Equal(saved, WindowPlacement.Fit(saved, [Laptop, Monitor], 1100, 700));
    }

    [Fact]
    public void OnTheExternalMonitor_IsKept()
    {
        WindowPlacement saved = new(3500, 50, 1200, 800, WindowMode.Maximized);
        Assert.Equal(saved, WindowPlacement.Fit(saved, [Laptop, Monitor], 1100, 700));
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
}
