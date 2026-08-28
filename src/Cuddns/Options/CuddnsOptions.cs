using System.Text.RegularExpressions;

namespace Cuddns.Options;

public sealed partial class CuddnsOptions
{
    public int IntervalSeconds { get; set; } = 300;

    public List<ProviderOptions> Providers { get; set; } = [];

    /// <summary>
    /// Validates the bound configuration, throwing a descriptive
    /// <see cref="ConfigValidationException"/> on the first problem found so startup fails fast.
    /// </summary>
    public void Validate()
    {
        if (IntervalSeconds <= 0)
        {
            throw new ConfigValidationException("intervalSeconds must be greater than 0.");
        }

        if (Providers.Count == 0)
        {
            throw new ConfigValidationException("At least one provider must be configured.");
        }

        for (var providerIndex = 0; providerIndex < Providers.Count; providerIndex++)
        {
            var provider = Providers[providerIndex];
            if (string.IsNullOrWhiteSpace(provider.Type))
            {
                throw new ConfigValidationException($"providers[{providerIndex}].type is required.");
            }

            if (provider.Zones.Count == 0)
            {
                throw new ConfigValidationException(
                    $"providers[{providerIndex}] ('{provider.Type}') must configure at least one zone.");
            }

            for (var zoneIndex = 0; zoneIndex < provider.Zones.Count; zoneIndex++)
            {
                var zone = provider.Zones[zoneIndex];
                var zonePath = $"providers[{providerIndex}].zones[{zoneIndex}]";

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
                    if (string.IsNullOrWhiteSpace(record) || !HostnameRegex().IsMatch(record))
                    {
                        throw new ConfigValidationException(
                            $"{zonePath}.records[{recordIndex}] ('{record}') is not a valid hostname.");
                    }
                }
            }
        }
    }

    [GeneratedRegex(@"^(?=.{1,253}$)(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*$")]
    private static partial Regex HostnameRegex();
}
