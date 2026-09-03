using Amazon.Route53;
using Amazon.Route53.Model;

namespace Cuddns.Providers.Route53;

public sealed class Route53DnsProvider : IDnsProvider, IDeletableDnsProvider, IDisposable
{
    private readonly IAmazonRoute53 _client;
    private readonly Route53ProviderConfig _config;
    private readonly Dictionary<ManagedRecord, Route53ZoneConfig> _zoneByRecord;

    public Route53DnsProvider(IAmazonRoute53 client, Route53ProviderConfig config)
    {
        _client = client;
        _config = config;
        var parsedRecords = config.Zones
            .SelectMany(zone => zone.Records.Select(record => (Spec: RecordSpec.Parse(record), Zone: zone)))
            .Select(x => (Record: new ManagedRecord(x.Spec.Name, x.Zone.Ttl, x.Spec.Type), x.Zone))
            .ToList();
        _zoneByRecord = parsedRecords.ToDictionary(x => x.Record, x => x.Zone);
        ManagedRecords = parsedRecords.Select(x => x.Record).ToList();
    }

    public string ProviderType => "route53";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public bool PruneRemovedRecords => _config.PruneRemovedRecords;

    public async Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken)
    {
        var zone = _zoneByRecord[record];
        var rrType = record.Type == RecordType.AAAA ? RRType.AAAA : RRType.A;

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = zone.HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Comment = $"Auto-update {record.Type} record for {record.Name} (Cuddns)",
                Changes =
                [
                    new Change
                    {
                        Action = ChangeAction.UPSERT,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Name = record.Name,
                            Type = rrType,
                            TTL = record.Ttl,
                            ResourceRecords = [new ResourceRecord { Value = ip }],
                        },
                    },
                ],
            },
        };

        await _client.ChangeResourceRecordSetsAsync(request, cancellationToken);
    }

    public string GetScope(ManagedRecord record) => _zoneByRecord[record].HostedZoneId;

    public bool OwnsScope(string scope) => _config.Zones.Any(z => z.HostedZoneId == scope);

    public async Task DeleteRecordAsync(
        ManagedRecord record, string scope, string lastKnownIp, CancellationToken cancellationToken)
    {
        var zone = _config.Zones.First(z => z.HostedZoneId == scope);
        var rrType = record.Type == RecordType.AAAA ? RRType.AAAA : RRType.A;

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = zone.HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Comment = $"Auto-delete {record.Type} record for {record.Name} (Cuddns, removed from config)",
                Changes =
                [
                    new Change
                    {
                        Action = ChangeAction.DELETE,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Name = record.Name,
                            Type = rrType,
                            // Route53 requires DELETE to match the existing rrset's TTL exactly;
                            // the removed record's original TTL isn't tracked in the cache, so we
                            // use the zone's current TTL (what would have been used to create it).
                            TTL = zone.Ttl,
                            ResourceRecords = [new ResourceRecord { Value = lastKnownIp }],
                        },
                    },
                ],
            },
        };

        await _client.ChangeResourceRecordSetsAsync(request, cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}
