using Amazon.Route53;
using Amazon.Route53.Model;

namespace Cuddns.Providers.Route53;

public sealed class Route53DnsProvider : IDnsProvider
{
    private readonly IAmazonRoute53 _client;
    private readonly Dictionary<string, Route53ZoneConfig> _zoneByRecord;

    public Route53DnsProvider(IAmazonRoute53 client, Route53ProviderConfig config)
    {
        _client = client;
        _zoneByRecord = config.Zones
            .SelectMany(zone => zone.Records.Select(record => (Record: record, Zone: zone)))
            .ToDictionary(x => x.Record, x => x.Zone);
        ManagedRecords = config.Zones
            .SelectMany(zone => zone.Records.Select(record => new ManagedRecord(record, zone.Ttl)))
            .ToList();
    }

    public string ProviderType => "route53";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public async Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken)
    {
        var zone = _zoneByRecord[record.Name];

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = zone.HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Comment = $"Auto-update A record for {record.Name} (Cuddns)",
                Changes =
                [
                    new Change
                    {
                        Action = ChangeAction.UPSERT,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Name = record.Name,
                            Type = RRType.A,
                            TTL = record.Ttl,
                            ResourceRecords = [new ResourceRecord { Value = ip }],
                        },
                    },
                ],
            },
        };

        await _client.ChangeResourceRecordSetsAsync(request, cancellationToken);
    }
}
