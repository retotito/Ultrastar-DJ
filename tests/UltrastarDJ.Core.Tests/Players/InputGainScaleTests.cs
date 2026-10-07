using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Core.Tests.Players;

public class InputGainScaleTests
{
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.1, -20.0)]
    [InlineData(10.0, 20.0)]
    [InlineData(0.7, -3.1)]       // an existing 0.7× setting shows as −3.1 dB
    [InlineData(0.0, -40.0)]      // silence clamps to the knob's left end
    [InlineData(0.001, -40.0)]
    [InlineData(100.0, 20.0)]
    public void ToDb_ClampsToTheKnobRange(double gain, double expectedDb) => Assert.Equal(expectedDb, InputGainScale.ToDb(gain), 1);

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-20.0, 0.1)]
    [InlineData(20.0, 10.0)]
    [InlineData(-60.0, 0.01)]     // clamped to −40 dB
    public void ToGain_ClampsToTheKnobRange(double db, double expectedGain) => Assert.Equal(expectedGain, InputGainScale.ToGain(db), 4);

    [Theory]
    [InlineData(-20.3, -20.5)]    // half-dB steps: fine enough to set, coarse enough to keep the settings file tidy
    [InlineData(-20.2, -20.0)]
    [InlineData(3.74, 3.5)]
    public void Snap_ToHalfDb(double db, double expected) => Assert.Equal(expected, InputGainScale.Snap(db), 3);

    [Fact]
    public void RoundTrip_KeepsTheSetting() => Assert.Equal(0.25, InputGainScale.ToGain(InputGainScale.ToDb(0.25)), 6);
}
