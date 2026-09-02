using Cuddns.Options;
using HostnameValidator = Cuddns.Validation.Hostname;

namespace Cuddns.Providers.Miab;

public sealed class MiabProviderConfig : IProviderConfig
{
    public string Type => "miab";

    public string? Hostname { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public List<string> Records { get; set; } = [];

    /// <summary>
    /// When true, a record that disappears from a still-configured box's <c>records</c>
    /// list is deleted from MiaB too, instead of just being left alone.
    /// </summary>
    public bool PruneRemovedRecords { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Hostname) || !HostnameValidator.IsValid(Hostname))
        {
            throw new ConfigValidationException("MiaB provider requires a valid box hostname to be configured.");
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new ConfigValidationException("MiaB provider requires a username to be configured.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new ConfigValidationException("MiaB provider requires a password to be configured.");
        }

        if (Records.Count == 0)
        {
            throw new ConfigValidationException("MiaB provider must configure at least one record.");
        }

        for (var recordIndex = 0; recordIndex < Records.Count; recordIndex++)
        {
            var record = Records[recordIndex];
            var (name, _) = RecordSpec.Parse(record);
            if (!HostnameValidator.IsValid(name))
            {
                throw new ConfigValidationException(
                    $"providers[miab].records[{recordIndex}] ('{record}') must be a valid hostname.");
            }
        }
    }
}
