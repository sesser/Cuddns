using Amazon.Route53;
using Amazon.Route53.Model;
using Cuddns.Options;

namespace Cuddns.Providers.Route53;

public sealed class Route53DnsProvider(IAmazonRoute53 client) : IDnsProvider
{
    public string ProviderType => "route53";

    public async Task UpsertRecordAsync(ZoneOptions zone, string recordName, string ip, CancellationToken cancellationToken)
    {
        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = zone.HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Comment = $"Auto-update A record for {recordName} (Cuddns)",
                Changes =
                [
                    new Change
                    {
                        Action = ChangeAction.UPSERT,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Name = recordName,
                            Type = RRType.A,
                            TTL = zone.Ttl,
                            ResourceRecords = [new ResourceRecord { Value = ip }],
                        },
                    },
                ],
            },
        };

        await client.ChangeResourceRecordSetsAsync(request, cancellationToken);
    }
}
