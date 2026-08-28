using Amazon;
using Amazon.Route53;
using Cuddns.Options;

namespace Cuddns.Providers.Route53;

public sealed class Route53DnsProviderFactory : IDnsProviderFactory
{
    public string ProviderType => "route53";

    public IDnsProvider Create(ProviderOptions provider)
    {
        if (string.IsNullOrWhiteSpace(provider.AccessKeyId) || string.IsNullOrWhiteSpace(provider.SecretAccessKey))
        {
            throw new InvalidOperationException(
                $"Route53 provider requires accessKeyId and secretAccessKey to be configured.");
        }

        var region = string.IsNullOrWhiteSpace(provider.Region)
            ? RegionEndpoint.USEast1
            : RegionEndpoint.GetBySystemName(provider.Region);

        var client = new AmazonRoute53Client(provider.AccessKeyId, provider.SecretAccessKey, region);
        return new Route53DnsProvider(client);
    }
}
