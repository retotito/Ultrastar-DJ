namespace UltrastarDJ.Core.Songbook;

/// <summary>One IPv4 address of the Mac, with what kind of network it is on.</summary>
public sealed record LocalAddress(string Ip, bool IsWireless, bool IsEthernet);

/// <summary>
/// The address that goes into the beamer's QR code: the one guests' phones can reach. A Mac has several (Wi-Fi,
/// Ethernet, VPN, virtual bridges); phones are on the Wi-Fi, so a home / party network address on Wi-Fi wins.
/// </summary>
public static class SongbookAddress
{
    public static string? Best(IEnumerable<LocalAddress> addresses)
        => addresses.Where(a => Rank(a) >= 0).OrderByDescending(Rank).ThenBy(a => a.Ip, StringComparer.Ordinal).FirstOrDefault()?.Ip;

    // Higher is better; -1 = never (self-assigned, carrier-grade NAT / Tailscale, not private).
    private static int Rank(LocalAddress a)
    {
        int[]? o = Octets(a.Ip);
        if (o is null)
        {
            return -1;
        }

        int range = (o[0], o[1]) switch
        {
            (192, 168) => 3,                               // home routers, phone hotspots
            (10, _) => 2,
            (172, >= 16 and <= 31) => 1,
            _ => -1,                                       // 169.254 link-local, 100.64/10, public
        };
        if (range < 0)
        {
            return -1;
        }

        int kind = a.IsWireless ? 20 : a.IsEthernet ? 10 : 0; // virtual bridges / VPN last
        return kind + range;
    }

    private static int[]? Octets(string ip)
    {
        string[] parts = ip.Split('.');
        if (parts.Length != 4)
        {
            return null;
        }

        int[] o = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out o[i]) || o[i] > 255)
            {
                return null;
            }
        }

        return o;
    }
}
