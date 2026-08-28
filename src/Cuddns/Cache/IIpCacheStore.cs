namespace Cuddns.Cache;

public interface IIpCacheStore
{
    Task<IReadOnlyDictionary<string, IpCacheEntry>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyDictionary<string, IpCacheEntry> entries, CancellationToken cancellationToken);
}
