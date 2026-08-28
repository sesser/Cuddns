using Cuddns.Options;

namespace Cuddns.Providers;

public interface IDnsProvider
{
    /// <summary>The <c>type</c> value in config (e.g. "route53") that selects this provider.</summary>
    string ProviderType { get; }

    /// <summary>Upserts an A record for <paramref name="recordName"/> to point at <paramref name="ip"/>.</summary>
    Task UpsertRecordAsync(ZoneOptions zone, string recordName, string ip, CancellationToken cancellationToken);
}
