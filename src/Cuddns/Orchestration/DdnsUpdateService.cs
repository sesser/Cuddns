using Cuddns.Cache;
using Cuddns.Providers;
using Cuddns.PublicIp;
using Microsoft.Extensions.Logging;

namespace Cuddns.Orchestration;

public sealed class DdnsUpdateService(
    IPublicIpResolver publicIpResolver,
    IIpCacheStore cacheStore,
    IReadOnlyList<IDnsProvider> dnsProviders,
    ILogger<DdnsUpdateService> logger)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var currentIps = await publicIpResolver.GetCurrentIpsAsync(cancellationToken);
        var cache = await cacheStore.LoadAsync(cancellationToken);
        var updated = new Dictionary<string, IpCacheEntry>(cache);

        foreach (var provider in dnsProviders)
        {
            foreach (var record in provider.ManagedRecords)
            {
                var currentIp = record.Type == RecordType.AAAA ? currentIps.IPv6 : currentIps.IPv4;
                if (currentIp is null)
                {
                    logger.LogWarning(
                        "Skipping {Record} ({Type}): no public address of that family available this run.",
                        record.Name, record.Type);
                    continue;
                }

                var cacheKey = CacheKey(record);
                if (cache.TryGetValue(cacheKey, out var entry) && entry.Ip == currentIp)
                {
                    logger.LogInformation(
                        "No update needed. {Record} ({Type}) already points to {Ip}", record.Name, record.Type, currentIp);
                    continue;
                }

                try
                {
                    await provider.UpsertRecordAsync(record, currentIp, cancellationToken);
                    updated[cacheKey] = new IpCacheEntry(currentIp, DateTimeOffset.UtcNow);
                    logger.LogInformation("Updated {Record} ({Type}) to {Ip}", record.Name, record.Type, currentIp);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed updating {Record} ({Type})", record.Name, record.Type);
                }
            }
        }

        await cacheStore.SaveAsync(updated, cancellationToken);
    }

    // A records keep the plain hostname as their cache key (unchanged from before AAAA
    // support existed) so upgrading doesn't invalidate every existing cache.json; AAAA
    // entries for the same hostname need a distinct key to track independently.
    private static string CacheKey(ManagedRecord record) =>
        record.Type == RecordType.A ? record.Name : $"{record.Name}|{record.Type}";
}
