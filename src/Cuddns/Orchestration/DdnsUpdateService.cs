using Cuddns.Cache;
using Cuddns.Providers;
using Cuddns.PublicIp;
using Microsoft.Extensions.Logging;

namespace Cuddns.Orchestration;

public sealed class DdnsUpdateService(
    IPublicIpProvider publicIpProvider,
    IIpCacheStore cacheStore,
    IReadOnlyList<IDnsProvider> dnsProviders,
    ILogger<DdnsUpdateService> logger)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var currentIp = await publicIpProvider.GetCurrentIpAsync(cancellationToken);
        var cache = await cacheStore.LoadAsync(cancellationToken);
        var updated = new Dictionary<string, IpCacheEntry>(cache);

        foreach (var provider in dnsProviders)
        {
            foreach (var record in provider.ManagedRecords)
            {
                if (cache.TryGetValue(record.Name, out var entry) && entry.Ip == currentIp)
                {
                    logger.LogInformation("No update needed. {Record} already points to {Ip}", record.Name, currentIp);
                    continue;
                }

                try
                {
                    await provider.UpsertRecordAsync(record, currentIp, cancellationToken);
                    updated[record.Name] = new IpCacheEntry(currentIp, DateTimeOffset.UtcNow);
                    logger.LogInformation("Updated {Record} to {Ip}", record.Name, currentIp);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed updating {Record}", record.Name);
                }
            }
        }

        await cacheStore.SaveAsync(updated, cancellationToken);
    }
}
