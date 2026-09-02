using Cuddns.Options;
using OtpNet;
using HostnameValidator = Cuddns.Validation.Hostname;

namespace Cuddns.Providers.Miab;

public sealed class MiabProviderConfig : IProviderConfig
{
    public string Type => "miab";

    public string? Hostname { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Optional base32 TOTP secret — the same shared secret shown when 2FA was enabled on
    /// this admin account (e.g. the value encoded in its setup QR code), not a live 6-digit
    /// code. Required only if the account has 2FA turned on; MiaB's API otherwise rejects
    /// every request with "missing-totp-token" since Basic auth alone isn't enough for it.
    /// </summary>
    public string? TotpSecret { get; set; }

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

        if (!string.IsNullOrWhiteSpace(TotpSecret))
        {
            try
            {
                Base32Encoding.ToBytes(TotpSecret);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                throw new ConfigValidationException("MiaB provider's totpSecret is not a valid base32 string.");
            }
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
