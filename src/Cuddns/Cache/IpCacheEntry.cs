namespace Cuddns.Cache;

/// <summary>
/// <paramref name="ProviderType"/> and <paramref name="Scope"/> are empty for providers
/// that don't implement deletion; when set, they let a later run route a delete call for
/// this record even after it's gone from every provider's ManagedRecords.
/// </summary>
public sealed record IpCacheEntry(string Ip, DateTimeOffset LastUpdatedUtc, string ProviderType = "", string Scope = "");
