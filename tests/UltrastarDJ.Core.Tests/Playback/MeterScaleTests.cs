using UltrastarDJ.Core.Playback;

namespace UltrastarDJ.Core.Tests.Playback;

public class MeterScaleTests
{
    private static readonly MeterScale Song = new(MinDb: -40, MaxDb: 0, WarnDb: -10, ClipDb: -4);

    [Theory]
    [InlineData(1.0, 1.0)]      // 0 dBFS = top
    [InlineData(0.1, 0.5)]      // −20 dB = halfway on −40…0
    [InlineData(0.01, 0.0)]     // −40 dB = floor
    [InlineData(0.001, 0.0)]    // below the floor stays at 0
    [InlineData(0.0, 0.0)]      // silence
    [InlineData(2.0, 1.0)]      // above the top is clamped
    public void Fraction_IsLinearInDb(double amplitude, double expected) => Assert.Equal(expected, Song.Fraction(amplitude), 3);

    [Fact]
    public void Fraction_FollowsTheElementsOwnRange()
    {
        MeterScale mic = new(MinDb: -70, MaxDb: -20, WarnDb: -30, ClipDb: -24);
        Assert.Equal(0.5, mic.Fraction(Math.Pow(10, -45 / 20.0)), 3);
    }

    [Fact]
    public void WarnAndClip_AreFractionsOnTheSameScale()
    {
        Assert.Equal(0.75, Song.WarnFraction, 3);
        Assert.Equal(0.9, Song.ClipFraction, 3);
    }

    [Theory]
    [InlineData(-30, 0.25)]
    [InlineData(-50, 0.0)]
    [InlineData(10, 1.0)]
    public void FractionOfDb_Clamps(double db, double expected) => Assert.Equal(expected, Song.FractionOfDb(db), 3);

    [Fact]
    public void Default_IsMusicRange() => Assert.Equal(new MeterScale(-40, 0, -8, -4), MeterScale.Music);

    [Fact]
    public void SongAndMicScales_PutYellowAndRedAtTheSamePlace()
    {
        // The Game Player shows them one under the other: the same number of yellow and red segments.
        MeterScale mic = new(-70, 0, -14, -7);
        Assert.Equal(MeterScale.Music.WarnFraction, mic.WarnFraction, 3);
        Assert.Equal(MeterScale.Music.ClipFraction, mic.ClipFraction, 3);
    }
}

public class VolumeCurveTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.125)]   // mpv's softvol is cubic: 50 % volume = 1/8 amplitude (−18 dB)
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    public void Mpv_IsCubic(double volume, double expected) => Assert.Equal(expected, VolumeCurve.MpvAmplitude(volume), 6);
}
