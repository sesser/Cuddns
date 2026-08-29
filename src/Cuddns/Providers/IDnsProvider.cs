namespace Cuddns.Providers;

public interface IDnsProvider
{
    /// <summary>The <c>type</c> value in config (e.g. "route53") that selects this provider.</summary>
    string ProviderType { get; }

    /// <summary>The records this provider instance is configured to keep up to date.</summary>
    IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    /// <summary>Upserts <paramref name="record"/> to point at <paramref name="ip"/>.</summary>
    Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken);
}
