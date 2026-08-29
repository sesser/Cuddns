using Amazon;
using Amazon.Route53;
using Microsoft.Extensions.Logging;

namespace Cuddns.Providers.Route53;

public sealed class Route53DnsProviderFactory(ILogger<Route53DnsProviderFactory> logger) : IDnsProviderFactory
{
    public string ProviderType => "route53";

    public Type ConfigType => typeof(Route53ProviderConfig);

    public IProviderConfig CreateDefaultConfig() => new Route53ProviderConfig();

    public IDnsProvider Create(IProviderConfig config)
    {
        var route53Config = (Route53ProviderConfig)config;

        var region = string.IsNullOrWhiteSpace(route53Config.Region)
            ? RegionEndpoint.USEast1
            : RegionEndpoint.GetBySystemName(route53Config.Region);

        logger.LogInformation(
            "Creating route53 provider (region: {Region}, zones: {ZoneCount}, records: {RecordCount})",
            region.SystemName, route53Config.Zones.Count, route53Config.Zones.Sum(z => z.Records.Count));

        var client = new AmazonRoute53Client(route53Config.AccessKeyId, route53Config.SecretAccessKey, region);
        return new Route53DnsProvider(client, route53Config);
    }
}
