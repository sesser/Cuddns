using Microsoft.Extensions.Logging;

namespace Cuddns.PublicIp;

/// <summary>
/// Resolves the current public IP per address family by trying each configured
/// <see cref="IPublicIpSource"/> in order until one answers, independently for IPv4 and IPv6
/// (a source that's IPv4-only, like ifconfig.net, simply falls through for IPv6 requests).
/// </summary>
public sealed class PublicIpResolver(
    IReadOnlyList<IPublicIpSource> sources, ILogger<PublicIpResolver> logger) : IPublicIpResolver
{
    public async Task<PublicIpResult> GetCurrentIpsAsync(CancellationToken cancellationToken)
    {
        var ipv4 = await ResolveAsync(IpFamily.IPv4, cancellationToken);
        var ipv6 = await ResolveAsync(IpFamily.IPv6, cancellationToken);

        if (ipv4 is null && ipv6 is null)
        {
            throw new InvalidOperationException(
                "Failed to determine the current public IP (IPv4 or IPv6) from any configured source.");
        }

        return new PublicIpResult(ipv4, ipv6);
    }

    private async Task<string?> ResolveAsync(IpFamily family, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            try
            {
                var ip = await source.TryGetIpAsync(family, cancellationToken);
                if (ip is not null)
                {
                    return ip;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Public IP source {Source} failed for {Family}; trying next source.", source.Name, family);
            }
        }

        return null;
    }
}
