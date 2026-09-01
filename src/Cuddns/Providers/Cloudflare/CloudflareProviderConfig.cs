using Cuddns.Options;
using Cuddns.Validation;

namespace Cuddns.Providers.Cloudflare;

public sealed class CloudflareProviderConfig : IProviderConfig
{
    public string Type => "cloudflare";

    public string? ApiToken { get; set; }

    public List<CloudflareZoneConfig> Zones { get; set; } = [];

    /// <summary>
    /// When true, a record that disappears from a still-configured zone's <c>records</c>
    /// list is deleted from Cloudflare too, instead of just being left alone.
    /// </summary>
    public bool PruneRemovedRecords { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiToken))
        {
            throw new ConfigValidationException("Cloudflare provider requires an apiToken to be configured.");
        }

        if (Zones.Count == 0)
        {
            throw new ConfigValidationException("Cloudflare provider must configure at least one zone.");
        }

        for (var zoneIndex = 0; zoneIndex < Zones.Count; zoneIndex++)
        {
            var zone = Zones[zoneIndex];
            var zonePath = $"providers[cloudflare].zones[{zoneIndex}]";

            if (string.IsNullOrWhiteSpace(zone.ZoneId))
            {
                throw new ConfigValidationException($"{zonePath}.zoneId is required.");
            }

            if (zone.Ttl <= 0)
            {
                throw new ConfigValidationException(
                    $"{zonePath}.ttl must be greater than 0 (use 1 for Cloudflare's \"automatic\" TTL).");
            }

            if (zone.Records.Count == 0)
            {
                throw new ConfigValidationException($"{zonePath}.records must contain at least one record.");
            }

            for (var recordIndex = 0; recordIndex < zone.Records.Count; recordIndex++)
            {
                var record = zone.Records[recordIndex];
                var (name, _) = RecordSpec.Parse(record);
                if (!Hostname.IsValid(name))
                {
                    throw new ConfigValidationException(
                        $"{zonePath}.records[{recordIndex}] ('{record}') is not a valid hostname.");
                }
            }
        }
    }
}
