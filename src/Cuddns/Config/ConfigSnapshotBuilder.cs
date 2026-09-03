using Cuddns.Providers;
using Cuddns.PublicIp;
using Microsoft.Extensions.Logging;

namespace Cuddns.Config;

/// <summary>
/// Builds a <see cref="ConfigSnapshot"/> from config.yaml/.env — the same construction
/// logic used both at startup and by <see cref="ConfigWatcherService"/> on every reload
/// attempt, so a reload behaves identically to a fresh start.
/// </summary>
public sealed class ConfigSnapshotBuilder(
    ConfigLoader configLoader,
    IReadOnlyDictionary<string, IDnsProviderFactory> catalogByType,
    IReadOnlyDictionary<string, IPublicIpSource> publicIpSourceCatalog,
    ILogger<PublicIpResolver> publicIpResolverLogger)
{
    /// <summary>
    /// Throws (typically <see cref="Options.ConfigValidationException"/>) on any problem —
    /// deliberately not caught here, so callers decide what to do with a failed load
    /// (startup lets it crash the process; a reload attempt catches and logs it).
    /// </summary>
    public ConfigSnapshot Build(string configPath, string? envPath)
    {
        var options = configLoader.Load(configPath, envPath);

        var dnsProviders = options.Providers
            .Select(providerConfig => catalogByType[providerConfig.Type].Create(providerConfig))
            .ToList();

        var sourceNames = options.PublicIpSources is { Count: > 0 }
            ? options.PublicIpSources
            : PublicIpSourceNames.All;
        var sources = sourceNames.Select(name => publicIpSourceCatalog[name]).ToList();
        var publicIpResolver = new PublicIpResolver(sources, options.EnableIpv6, publicIpResolverLogger);

        return new ConfigSnapshot(options, dnsProviders, publicIpResolver);
    }
}
