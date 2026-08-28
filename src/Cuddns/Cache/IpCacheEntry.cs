namespace Cuddns.Cache;

public sealed record IpCacheEntry(string Ip, DateTimeOffset LastUpdatedUtc);
