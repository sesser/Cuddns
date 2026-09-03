using Cuddns.Cache;
using Cuddns.Providers;
using Cuddns.PublicIp;
using Microsoft.Extensions.Logging;

namespace Cuddns.Orchestration;

public sealed class DdnsUpdateService(IIpCacheStore cacheStore, ILogger<DdnsUpdateService> logger)
{
    public async Task RunOnceAsync(
        IReadOnlyList<IDnsProvider> dnsProviders,
        IPublicIpResolver publicIpResolver,
        CancellationToken cancellationToken)
    {
        var cache = await cacheStore.LoadAsync(cancellationToken);
        var updated = new Dictionary<string, IpCacheEntry>(cache);

        await ReconcileRemovedRecordsAsync(dnsProviders, cache, updated, cancellationToken);

        if (dnsProviders.Sum(p => p.ManagedRecords.Count) == 0)
        {
            // Nothing to resolve an IP for — e.g. every provider was intentionally emptied
            // out to (temporarily) stop managing anything. Skip the public IP lookup
            // entirely rather than spending a network call on every run for no reason.
            logger.LogInformation("No records configured across any provider; skipping the public IP lookup this run.");
            await cacheStore.SaveAsync(updated, cancellationToken);
            return;
        }

        var currentIps = await publicIpResolver.GetCurrentIpsAsync(cancellationToken);

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
                    var scope = (provider as IDeletableDnsProvider)?.GetScope(record) ?? "";
                    updated[cacheKey] = new IpCacheEntry(currentIp, DateTimeOffset.UtcNow, provider.ProviderType, scope);
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

    /// <summary>
    /// Deletes (or otherwise reconciles) cache entries for records that were removed from
    /// config since the last run — see <see cref="IDeletableDnsProvider"/> for the opt-in
    /// deletion contract and its boundaries.
    /// </summary>
    private async Task ReconcileRemovedRecordsAsync(
        IReadOnlyList<IDnsProvider> dnsProviders,
        IReadOnlyDictionary<string, IpCacheEntry> cache,
        Dictionary<string, IpCacheEntry> updated,
        CancellationToken cancellationToken)
    {
        var activeKeys = dnsProviders
            .SelectMany(provider => provider.ManagedRecords.Select(CacheKey))
            .ToHashSet();

        if (cache.Count > 0 && activeKeys.Count == 0)
        {
            logger.LogWarning(
                "Loaded config has no managed records but cache.json is not empty; skipping delete " +
                "reconciliation this run — check config.yaml before the next run.");
            return;
        }

        foreach (var (key, entry) in cache)
        {
            if (activeKeys.Contains(key))
            {
                continue;
            }

            var owner = dnsProviders
                .OfType<IDeletableDnsProvider>()
                .FirstOrDefault(p => p.ProviderType == entry.ProviderType && p.OwnsScope(entry.Scope));

            if (owner is null || !owner.PruneRemovedRecords)
            {
                logger.LogInformation(
                    "{Record} is no longer configured; leaving it at the provider " +
                    "(pruneRemovedRecords is off, or its provider/zone is no longer configured).", key);
                updated.Remove(key);
                continue;
            }

            var record = ParseCacheKey(key);
            try
            {
                await owner.DeleteRecordAsync(record, entry.Scope, entry.Ip, cancellationToken);
                logger.LogInformation("Deleted {Record} ({Type}) from {ProviderType} (removed from config)", record.Name, record.Type, entry.ProviderType);
                updated.Remove(key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed deleting {Record} ({Type}); will retry next run", record.Name, record.Type);
            }
        }
    }

    private static ManagedRecord ParseCacheKey(string key)
    {
        var separatorIndex = key.IndexOf('|');
        return separatorIndex < 0
            ? new ManagedRecord(key, 0, RecordType.A)
            : new ManagedRecord(key[..separatorIndex], 0, Enum.Parse<RecordType>(key[(separatorIndex + 1)..]));
    }
}
