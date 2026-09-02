using Cuddns.Options;
using Cuddns.Providers.Miab;
using FluentAssertions;

namespace Cuddns.Tests.Providers.Miab;

public class MiabProviderConfigTests
{
    private static MiabProviderConfig BuildConfig(
        string? hostname = "box.example.com",
        string? username = "admin@example.com",
        string? password = "test-pass",
        params string[] records)
    {
        return new MiabProviderConfig
        {
            Hostname = hostname,
            Username = username,
            Password = password,
            Records = records.Length == 0 ? ["home.example.com"] : [.. records],
        };
    }

    [Fact]
    public void Validate_ValidConfig_DoesNotThrow()
    {
        var act = () => BuildConfig().Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MissingHostname_Throws()
    {
        var act = () => BuildConfig(hostname: null).Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*hostname*");
    }

    [Fact]
    public void Validate_InvalidHostname_Throws()
    {
        var act = () => BuildConfig(hostname: "not a hostname!").Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*hostname*");
    }

    [Fact]
    public void Validate_MissingUsername_Throws()
    {
        var act = () => BuildConfig(username: null).Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*username*");
    }

    [Fact]
    public void Validate_MissingPassword_Throws()
    {
        var act = () => BuildConfig(password: null).Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*password*");
    }

    [Fact]
    public void Validate_NoRecords_DoesNotThrow()
    {
        // An empty records list has to stay valid — it's the only way to prune the very
        // last record via pruneRemovedRecords, since the provider block itself must remain
        // configured for Cuddns to still own the scope needed to reach it.
        var config = BuildConfig();
        config.Records = [];

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_InvalidRecordHostname_Throws()
    {
        var act = () => BuildConfig(records: "not a hostname!").Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*records[0]*");
    }

    [Fact]
    public void Validate_NoTotpSecret_DoesNotThrow()
    {
        var config = BuildConfig();
        config.TotpSecret = null;

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ValidTotpSecret_DoesNotThrow()
    {
        var config = BuildConfig();
        config.TotpSecret = "JBSWY3DPEHPK3PXP";

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_InvalidTotpSecret_Throws()
    {
        var config = BuildConfig();
        config.TotpSecret = "not-valid-base32!!!";

        var act = () => config.Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*totpSecret*");
    }
}
