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
    public void Validate_NoRecords_Throws()
    {
        var config = BuildConfig();
        config.Records = [];

        var act = () => config.Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*record*");
    }

    [Fact]
    public void Validate_InvalidRecordHostname_Throws()
    {
        var act = () => BuildConfig(records: "not a hostname!").Validate();

        act.Should().Throw<ConfigValidationException>().WithMessage("*records[0]*");
    }
}
