using UltrastarDJ.Infrastructure.Songbook;

namespace UltrastarDJ.Infrastructure.Tests;

public class CloudflareTunnelTests
{
    [Theory]
    [InlineData("2026-10-07T11:43:20Z INF |  https://tired-camel-river-lamp.trycloudflare.com                                   |", "https://tired-camel-river-lamp.trycloudflare.com")]
    [InlineData("INF Requesting new quick Tunnel on trycloudflare.com...", null)]
    [InlineData("INF +--------------------------------------------------------------------------------------------+", null)]
    [InlineData("ERR failed to request quick Tunnel: Post \"https://api.trycloudflare.com/tunnel\": dial tcp", null)]
    [InlineData(null, null)]
    public void FindUrl_OnlyTheAnnouncedAddress(string? line, string? url)
    {
        Assert.Equal(url, CloudflareTunnel.FindUrl(line));
    }
}
