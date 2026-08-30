using Cuddns.Options;
using Cuddns.Validation;

namespace Cuddns.Providers.NoIp;

public sealed class NoIpProviderConfig : IProviderConfig
{
    public string Type => "noip";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public List<string> Records { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new ConfigValidationException("No-IP provider requires a username to be configured.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new ConfigValidationException("No-IP provider requires a password to be configured.");
        }

        if (Records.Count == 0)
        {
            throw new ConfigValidationException("No-IP provider must configure at least one record.");
        }

        for (var recordIndex = 0; recordIndex < Records.Count; recordIndex++)
        {
            var record = Records[recordIndex];
            if (!Hostname.IsValid(record))
            {
                throw new ConfigValidationException(
                    $"providers[noip].records[{recordIndex}] ('{record}') must be a valid hostname.");
            }
        }
    }
}
