using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Core.Tests.Players;

public class MicActivityTests
{
    [Theory]
    [InlineData(0.0, 0.003, 0.0)]
    [InlineData(0.002, 0.003, 0.0)]     // under the gate: room noise
    [InlineData(0.003, 0.003, 0.0)]
    [InlineData(0.0949, 0.003, 1.0)]    // 30 dB above the gate
    [InlineData(1.0, 0.003, 1.0)]       // clamped
    public void Level_IsDecibelsAboveTheGate(double rms, double gate, double expected)
    {
        Assert.Equal(expected, MicActivity.Level(rms, gate), 2);
    }

    [Fact]
    public void Level_HalfwayAt15DbAboveTheGate()
    {
        Assert.Equal(0.5, MicActivity.Level(0.003 * Math.Pow(10, 15 / 20.0), 0.003), 3);
    }
}
