using System.Net;
using System.Net.Sockets;

namespace Cuddns.PublicIp;

/// <summary>
/// Base for public-IP sources that expose separate, address-family-pinned HTTP endpoints
/// (unlike ifconfig.net's single ambiguous one) — hitting the right endpoint is what makes
/// IPv6 detection reliable instead of guessing at whichever family the connection used.
/// </summary>
public abstract class FamilyPinnedHttpPublicIpSource(HttpClient httpClient) : IPublicIpSource
{
    public abstract string Name { get; }

    protected abstract string GetUrl(IpFamily family);

    public async Task<string?> TryGetIpAsync(IpFamily family, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetStringAsync(GetUrl(family), cancellationToken);
        var ip = response.Trim();

        if (!IPAddress.TryParse(ip, out var parsed) || !MatchesFamily(parsed, family))
        {
            throw new InvalidOperationException(
                $"{Name} returned an unexpected response for {family}: '{Truncate(ip)}'");
        }

        return ip;
    }

    private static bool MatchesFamily(IPAddress address, IpFamily family) => family switch
    {
        IpFamily.IPv4 => address.AddressFamily == AddressFamily.InterNetwork,
        IpFamily.IPv6 => address.AddressFamily == AddressFamily.InterNetworkV6,
        _ => false,
    };

    private static string Truncate(string value) => value.Length <= 100 ? value : value[..100] + "...";
}
