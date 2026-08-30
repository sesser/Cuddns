namespace Cuddns.PublicIp;

/// <summary>
/// A single public-IP echo source, queried independently per address family. One of
/// potentially several configured sources (see <see cref="PublicIpSourceNames"/> and
/// <see cref="IPublicIpResolver"/>), so a source that can't reach a network, doesn't
/// support a family, or returns something unusable is just a fallback trigger, not a
/// hard failure.
/// </summary>
public interface IPublicIpSource
{
    /// <summary>The name used to select this source in config (e.g. "ipify").</summary>
    string Name { get; }

    /// <summary>
    /// Returns the current public IP for <paramref name="family"/>, or null if this source
    /// has no answer for that family (e.g. no IPv6 route). Throws if it attempted the lookup
    /// and got back something it can't trust.
    /// </summary>
    Task<string?> TryGetIpAsync(IpFamily family, CancellationToken cancellationToken);
}
