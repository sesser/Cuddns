namespace Cuddns.Providers;

/// <summary>
/// Opt-in capability for providers whose API can actually delete a record. Only
/// implemented by providers where that's true (Route53, Cloudflare) — DuckDNS and No-IP
/// have no delete verb in their update APIs, so they don't implement this at all.
/// </summary>
public interface IDeletableDnsProvider : IDnsProvider
{
    /// <summary>Whether this provider instance is configured to delete records that disappear from its config.</summary>
    bool PruneRemovedRecords { get; }

    /// <summary>
    /// Opaque, stable identifier for the zone/scope that owns <paramref name="record"/>
    /// (Route53 hosted zone id, Cloudflare zone id). Persisted alongside the record so it
    /// can still be routed to the right zone after it's gone from <see cref="IDnsProvider.ManagedRecords"/>.
    /// </summary>
    string GetScope(ManagedRecord record);

    /// <summary>
    /// True if this provider instance currently has a configured zone matching <paramref name="scope"/>,
    /// even if the specific record was removed from that zone's <c>records</c> list.
    /// </summary>
    bool OwnsScope(string scope);

    /// <summary>
    /// Deletes <paramref name="record"/> from the zone identified by <paramref name="scope"/>.
    /// <paramref name="lastKnownIp"/> is the record's last-known value (some providers, e.g.
    /// Route53, require the exact current value to delete a record set).
    /// </summary>
    Task DeleteRecordAsync(ManagedRecord record, string scope, string lastKnownIp, CancellationToken cancellationToken);
}
