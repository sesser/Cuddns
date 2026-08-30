using Cuddns.Providers;
using Cuddns.PublicIp;

namespace Cuddns.Options;

public sealed class CuddnsOptions
{
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to look up (and act on) a public IPv6 address at all. Defaults to false: no
    /// provider consumes AAAA records yet, so attempting IPv6 lookups by default would just be
    /// wasted network calls (and log noise) for the common case of a host without IPv6.
    /// </summary>
    public bool EnableIpv6 { get; set; }

    /// <summary>
    /// Ordered public-IP sources to try, by name (see <see cref="PublicIpSourceNames"/>).
    /// Null/empty means "use the built-in default order" (<see cref="PublicIpSourceNames.All"/>).
    /// </summary>
    public List<string>? PublicIpSources { get; set; }

    public List<IProviderConfig> Providers { get; set; } = [];

    /// <summary>
    /// Validates the bound configuration, throwing a descriptive
    /// <see cref="ConfigValidationException"/> on the first problem found so startup fails fast.
    /// </summary>
    public void Validate()
    {
        if (IntervalSeconds <= 0)
        {
            throw new ConfigValidationException("intervalSeconds must be greater than 0.");
        }

        if (PublicIpSources is { Count: > 0 })
        {
            foreach (var name in PublicIpSources)
            {
                if (!PublicIpSourceNames.All.Contains(name))
                {
                    throw new ConfigValidationException(
                        $"Unknown publicIpSources entry '{name}'. Known sources: {string.Join(", ", PublicIpSourceNames.All)}.");
                }
            }
        }

        if (Providers.Count == 0)
        {
            throw new ConfigValidationException("At least one provider must be configured.");
        }

        foreach (var provider in Providers)
        {
            provider.Validate();
        }
    }
}
