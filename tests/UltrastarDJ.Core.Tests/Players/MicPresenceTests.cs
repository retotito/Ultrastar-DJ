using UltrastarDJ.Core.Players;

namespace UltrastarDJ.Core.Tests.Players;

public class MicPresenceTests
{
    private static PlayerConfig P(int id, string? device, MicChannelSide side = MicChannelSide.Left)
        => PlayerConfig.Default(id) with { Mic = device is null ? null : new MicBinding(device, side) };

    private static readonly PlayerConfig[] Players =
    [
        P(1, "SingStar", MicChannelSide.Left),
        P(2, "SingStar", MicChannelSide.Right),
        P(3, "USB Mic"),
        P(4, null),
    ];

    [Fact]
    public void Missing_DeviceGone_ListsEveryPlayerOnIt()
    {
        IReadOnlyList<PlayerConfig> missing = MicPresence.Missing(Players, new HashSet<string> { "USB Mic" });

        Assert.Equal([1, 2], missing.Select(p => p.Id));
    }

    [Fact]
    public void Missing_PlayerWithoutMic_IsNeverMissing()
    {
        Assert.Empty(MicPresence.Missing(Players, new HashSet<string> { "SingStar", "USB Mic" }));
    }

    [Fact]
    public void Diff_ReportsAddedAndRemoved()
    {
        MicPresence.Change c = MicPresence.Diff(new HashSet<string> { "SingStar", "Built-in" }, new HashSet<string> { "Built-in", "USB Mic" });

        Assert.Equal(["USB Mic"], c.Added);
        Assert.Equal(["SingStar"], c.Removed);
        Assert.False(c.IsEmpty);
    }

    [Fact]
    public void Diff_SameSets_IsEmpty()
    {
        Assert.True(MicPresence.Diff(new HashSet<string> { "A" }, new HashSet<string> { "A" }).IsEmpty);
    }

    [Fact]
    public void On_FindsPlayersBoundToDevices()
    {
        Assert.Equal([3], MicPresence.On(Players, ["USB Mic"]).Select(p => p.Id));
        Assert.Equal([1, 2], MicPresence.On(Players, ["SingStar"]).Select(p => p.Id));
    }

    [Fact]
    public void Describe_NamesPlayerAndDevice()
    {
        Assert.Equal("Player 2 (SingStar, right)", MicPresence.Describe(Players[1]));
    }
}

public class MicBindingTests
{
    private static MicBinding B(string dev, MicChannelSide side) => new(dev, side);

    [Theory]
    [InlineData(MicChannelSide.Left, MicChannelSide.Left, true)]
    [InlineData(MicChannelSide.Left, MicChannelSide.Right, false)]  // L and R of one dongle: two singers
    [InlineData(MicChannelSide.Mono, MicChannelSide.Left, true)]    // mono uses both sides
    [InlineData(MicChannelSide.Right, MicChannelSide.Mono, true)]
    [InlineData(MicChannelSide.Mono, MicChannelSide.Mono, true)]
    public void ConflictsWith_SameDevice(MicChannelSide a, MicChannelSide b, bool expected)
        => Assert.Equal(expected, B("SingStar", a).ConflictsWith(B("SingStar", b)));

    [Fact]
    public void ConflictsWith_OtherDevice_Never()
        => Assert.False(B("SingStar", MicChannelSide.Mono).ConflictsWith(B("SingStar (2)", MicChannelSide.Mono)));
}
