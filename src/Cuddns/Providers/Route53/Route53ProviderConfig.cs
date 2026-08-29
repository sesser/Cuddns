using Cuddns.Options;
using Cuddns.Validation;

namespace Cuddns.Providers.Route53;

public sealed class Route53ProviderConfig : IProviderConfig
{
    public string Type => "route53";

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? Region { get; set; }

    public List<Route53ZoneConfig> Zones { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessKeyId) || string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            throw new ConfigValidationException(
                "Route53 provider requires accessKeyId and secretAccessKey to be configured.");
        }

        if (Zones.Count == 0)
        {
            throw new ConfigValidationException("Route53 provider must configure at least one zone.");
        }

        for (var zoneIndex = 0; zoneIndex < Zones.Count; zoneIndex++)
        {
            var zone = Zones[zoneIndex];
            var zonePath = $"providers[route53].zones[{zoneIndex}]";

            if (string.IsNullOrWhiteSpace(zone.HostedZoneId))
            {
                throw new ConfigValidationException($"{zonePath}.hostedZoneId is required.");
            }

            if (zone.Ttl <= 0)
            {
                throw new ConfigValidationException($"{zonePath}.ttl must be greater than 0.");
            }

            if (zone.Records.Count == 0)
            {
                throw new ConfigValidationException($"{zonePath}.records must contain at least one record.");
            }

            for (var recordIndex = 0; recordIndex < zone.Records.Count; recordIndex++)
            {
                var record = zone.Records[recordIndex];
                if (!Hostname.IsValid(record))
                {
                    throw new ConfigValidationException(
                        $"{zonePath}.records[{recordIndex}] ('{record}') is not a valid hostname.");
                }
            }
        }
    }
}
