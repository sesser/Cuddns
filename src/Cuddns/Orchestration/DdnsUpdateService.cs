using Cuddns.Cache;
using Cuddns.Options;
using Cuddns.Providers;
using Cuddns.PublicIp;
using Microsoft.Extensions.Logging;

namespace Cuddns.Orchestration;

public sealed class DdnsUpdateService(
    IPublicIpProvider publicIpProvider,
    IIpCacheStore cacheStore,
    IReadOnlyDictionary<string, IDnsProviderFactory> providerFactories,
    ILogger<DdnsUpdateService> logger)
{
    public async Task RunOnceAsync(CuddnsOptions config, CancellationToken cancellationToken)
    {
        var currentIp = await publicIpProvider.GetCurrentIpAsync(cancellationToken);
        var cache = await cacheStore.LoadAsync(cancellationToken);
        var updated = new Dictionary<string, IpCacheEntry>(cache);

        foreach (var provider in config.Providers)
        {
            if (!providerFactories.TryGetValue(provider.Type, out var factory))
            {
                logger.LogError("No provider registered for type '{ProviderType}'; skipping.", provider.Type);
                continue;
            }

            IDnsProvider dnsProvider;
            try
            {
                dnsProvider = factory.Create(provider);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize provider '{ProviderType}'; skipping.", provider.Type);
                continue;
            }

            foreach (var zone in provider.Zones)
            {
                foreach (var record in zone.Records)
                {
                    if (cache.TryGetValue(record, out var entry) && entry.Ip == currentIp)
                    {
                        logger.LogInformation("No update needed. {Record} already points to {Ip}", record, currentIp);
                        continue;
                    }

                    try
                    {
                        await dnsProvider.UpsertRecordAsync(zone, record, currentIp, cancellationToken);
                        updated[record] = new IpCacheEntry(currentIp, DateTimeOffset.UtcNow);
                        logger.LogInformation("Updated {Record} to {Ip}", record, currentIp);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed updating {Record}", record);
                    }
                }
            }
        }

        await cacheStore.SaveAsync(updated, cancellationToken);
    }
}
