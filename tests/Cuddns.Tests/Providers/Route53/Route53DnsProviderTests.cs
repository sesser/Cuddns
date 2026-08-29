using Amazon.Route53;
using Amazon.Route53.Model;
using Cuddns.Providers;
using Cuddns.Providers.Route53;
using FluentAssertions;
using Moq;

namespace Cuddns.Tests.Providers.Route53;

public class Route53DnsProviderTests
{
    private readonly Mock<IAmazonRoute53> _client = new();

    private static Route53ProviderConfig BuildConfig(params (string ZoneId, int Ttl, string[] Records)[] zones)
    {
        return new Route53ProviderConfig
        {
            AccessKeyId = "unused",
            SecretAccessKey = "unused",
            Zones = zones.Select(z => new Route53ZoneConfig
            {
                HostedZoneId = z.ZoneId,
                Ttl = z.Ttl,
                Records = [.. z.Records],
            }).ToList(),
        };
    }

    [Fact]
    public void ManagedRecords_FlattensAllZonesWithTheirOwnTtl()
    {
        var config = BuildConfig(
            ("Z1", 300, ["a.example.com", "b.example.com"]),
            ("Z2", 60, ["c.example.com"]));

        var provider = new Cuddns.Providers.Route53.Route53DnsProvider(_client.Object, config);

        provider.ManagedRecords.Should().BeEquivalentTo(
        [
            new ManagedRecord("a.example.com", 300),
            new ManagedRecord("b.example.com", 300),
            new ManagedRecord("c.example.com", 60),
        ]);
    }

    [Fact]
    public async Task UpsertRecordAsync_RoutesToOwningZoneWithCorrectTtl()
    {
        var config = BuildConfig(
            ("Z1", 300, ["a.example.com"]),
            ("Z2", 60, ["b.example.com"]));

        ChangeResourceRecordSetsRequest? capturedRequest = null;
        _client.Setup(c => c.ChangeResourceRecordSetsAsync(
                It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChangeResourceRecordSetsRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new ChangeResourceRecordSetsResponse());

        var provider = new Cuddns.Providers.Route53.Route53DnsProvider(_client.Object, config);
        var record = provider.ManagedRecords.Single(r => r.Name == "b.example.com");

        await provider.UpsertRecordAsync(record, "203.0.113.10", CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.HostedZoneId.Should().Be("Z2");
        var change = capturedRequest.ChangeBatch.Changes.Single().ResourceRecordSet;
        change.Name.Should().Be("b.example.com");
        change.TTL.Should().Be(60);
        change.ResourceRecords.Single().Value.Should().Be("203.0.113.10");
    }
}
