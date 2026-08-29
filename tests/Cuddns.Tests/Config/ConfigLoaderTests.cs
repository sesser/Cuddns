using Cuddns.Config;
using Cuddns.Options;
using Cuddns.Providers;
using Cuddns.Providers.Route53;
using FluentAssertions;

namespace Cuddns.Tests.Config;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("cuddns-config-tests-").FullName;
    private readonly List<string> _envVarsToClear = [];
    private readonly IDnsProviderFactory[] _catalog = [new Route53DnsProviderFactory()];

    private string ConfigPath => Path.Combine(_tempDir, "config.yaml");
    private string EnvPath => Path.Combine(_tempDir, ".env");

    private ConfigLoader CreateSut() => new(_catalog);

    private void SetEnvVar(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _envVarsToClear.Add(name);
    }

    [Fact]
    public async Task Load_ValidYamlWithEnvVarsResolved_BindsCorrectly()
    {
        SetEnvVar("CUDDNS_TEST_ACCESS_KEY", "AKIA_TEST_VALUE");
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 120
            providers:
              - type: route53
                accessKeyId: ${CUDDNS_TEST_ACCESS_KEY}
                secretAccessKey: unused-in-this-test
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - auth.example.com
                      - www.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        options.IntervalSeconds.Should().Be(120);
        options.Providers.Should().ContainSingle();
        var provider = options.Providers[0].Should().BeOfType<Route53ProviderConfig>().Subject;
        provider.Type.Should().Be("route53");
        provider.AccessKeyId.Should().Be("AKIA_TEST_VALUE");
        provider.Zones.Should().ContainSingle();
        provider.Zones[0].HostedZoneId.Should().Be("Z123");
        provider.Zones[0].Records.Should().Equal("auth.example.com", "www.example.com");
    }

    [Fact]
    public async Task Load_MissingHostedZoneId_ThrowsNamingField()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - ttl: 300
                    records:
                      - a.example.com
            """);

        var act = () => CreateSut().Load(ConfigPath, envPath: null);

        act.Should().Throw<ConfigValidationException>()
            .WithMessage("*hostedZoneId*");
    }

    [Fact]
    public async Task Load_UnresolvedEnvVar_ThrowsNamingVariable()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: ${CUDDNS_TEST_DOES_NOT_EXIST}
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
            """);

        var act = () => CreateSut().Load(ConfigPath, envPath: null);

        act.Should().Throw<ConfigValidationException>()
            .WithMessage("*CUDDNS_TEST_DOES_NOT_EXIST*");
    }

    [Fact]
    public async Task Load_EnvFileValues_ArePickedUpAndSubstituted()
    {
        await File.WriteAllTextAsync(EnvPath, "CUDDNS_TEST_SECRET=from-dot-env-file");
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: ${CUDDNS_TEST_SECRET}
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
            """);
        _envVarsToClear.Add("CUDDNS_TEST_SECRET");

        var options = CreateSut().Load(ConfigPath, EnvPath);

        var provider = options.Providers[0].Should().BeOfType<Route53ProviderConfig>().Subject;
        provider.SecretAccessKey.Should().Be("from-dot-env-file");
    }

    [Fact]
    public async Task Load_UnknownProviderType_ThrowsNamingType()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: cloudflare
                apiToken: unused
            """);

        var act = () => CreateSut().Load(ConfigPath, envPath: null);

        act.Should().Throw<ConfigValidationException>()
            .WithMessage("*cloudflare*");
    }

    public void Dispose()
    {
        foreach (var name in _envVarsToClear)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
