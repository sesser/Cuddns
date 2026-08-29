using Amazon;
using Amazon.Route53;

namespace Cuddns.Providers.Route53;

public sealed class Route53DnsProviderFactory : IDnsProviderFactory
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

        var client = new AmazonRoute53Client(route53Config.AccessKeyId, route53Config.SecretAccessKey, region);
        return new Route53DnsProvider(client, route53Config);
    }
}
