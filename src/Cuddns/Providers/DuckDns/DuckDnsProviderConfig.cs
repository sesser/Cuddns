using Cuddns.Options;
using Cuddns.Validation;

namespace Cuddns.Providers.DuckDns;

public sealed class DuckDnsProviderConfig : IProviderConfig
{
    public string Type => "duckdns";

    public string? Token { get; set; }

    public List<string> Records { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            throw new ConfigValidationException("DuckDNS provider requires a token to be configured.");
        }

        if (Records.Count == 0)
        {
            throw new ConfigValidationException("DuckDNS provider must configure at least one record.");
        }

        for (var recordIndex = 0; recordIndex < Records.Count; recordIndex++)
        {
            var record = Records[recordIndex];
            if (!Hostname.IsValid(record) || !record.EndsWith(".duckdns.org", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigValidationException(
                    $"providers[duckdns].records[{recordIndex}] ('{record}') must be a valid *.duckdns.org hostname.");
            }
        }
    }
}
