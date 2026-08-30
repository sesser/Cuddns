namespace Cuddns.PublicIp;

public interface IPublicIpResolver
{
    /// <summary>
    /// Resolves the current public IPv4 and IPv6 addresses, in one pass across the configured
    /// sources. Throws only if no source could determine either address; a missing single
    /// family is returned as null on <see cref="PublicIpResult"/>.
    /// </summary>
    Task<PublicIpResult> GetCurrentIpsAsync(CancellationToken cancellationToken);
}
