using Cuddns.Options;
using Cuddns.Providers;
using Cuddns.PublicIp;

namespace Cuddns.Config;

/// <summary>
/// Everything derived from config.yaml/.env as of one successful load, swapped as a single
/// atomic unit (see <see cref="ConfigState"/>) so nothing ever observes providers from one
/// config alongside resolver settings from another.
/// </summary>
public sealed record ConfigSnapshot(
    CuddnsOptions Options,
    IReadOnlyList<IDnsProvider> DnsProviders,
    IPublicIpResolver PublicIpResolver);
