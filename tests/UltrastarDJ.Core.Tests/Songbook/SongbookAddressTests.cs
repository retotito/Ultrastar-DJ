using UltrastarDJ.Core.Songbook;

namespace UltrastarDJ.Core.Tests.Songbook;

public class SongbookAddressTests
{
    [Fact]
    public void WifiHomeNetwork_Wins()
    {
        LocalAddress[] all =
        [
            new("10.211.55.2", false, false),       // Parallels bridge
            new("192.168.1.23", true, false),       // Wi-Fi
            new("192.168.2.5", false, true),        // Ethernet
        ];

        Assert.Equal("192.168.1.23", SongbookAddress.Best(all));
    }

    [Fact]
    public void NeverLinkLocalOrTailscale()
    {
        Assert.Null(SongbookAddress.Best([new("169.254.10.1", true, false), new("100.101.3.4", false, false)]));
    }

    [Fact]
    public void EthernetWhenThereIsNoWifi()
    {
        Assert.Equal("10.0.0.7", SongbookAddress.Best([new("10.0.0.7", false, true), new("172.20.0.1", false, false)]));
    }

    [Fact]
    public void GarbageIsIgnored()
    {
        Assert.Null(SongbookAddress.Best([new("fe80::1", true, false), new("300.1.1.1", true, false)]));
    }
}
