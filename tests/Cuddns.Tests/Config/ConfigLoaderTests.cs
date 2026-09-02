using Cuddns.Config;
using Cuddns.Options;
using Cuddns.Providers;
using Cuddns.Providers.Cloudflare;
using Cuddns.Providers.DuckDns;
using Cuddns.Providers.Miab;
using Cuddns.Providers.NoIp;
using Cuddns.Providers.Route53;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cuddns.Tests.Config;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("cuddns-config-tests-").FullName;
    private readonly List<string> _envVarsToClear = [];
    private readonly IDnsProviderFactory[] _catalog =
    [
        new Route53DnsProviderFactory(NullLogger<Route53DnsProviderFactory>.Instance),
        new DuckDnsProviderFactory(NullLogger<DuckDnsProviderFactory>.Instance),
        new CloudflareDnsProviderFactory(NullLogger<CloudflareDnsProviderFactory>.Instance),
        new NoIpProviderFactory(NullLogger<NoIpProviderFactory>.Instance),
        new MiabDnsProviderFactory(NullLogger<MiabDnsProviderFactory>.Instance),
    ];

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
    public async Task Load_MultipleProviderTypes_BindsEachIntoItsOwnConcreteType()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
              - type: duckdns
                token: test-token
                records:
                  - home.duckdns.org
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        options.Providers.Should().HaveCount(2);
        options.Providers[0].Should().BeOfType<Route53ProviderConfig>();
        var duckDns = options.Providers[1].Should().BeOfType<DuckDnsProviderConfig>().Subject;
        duckDns.Token.Should().Be("test-token");
        duckDns.Records.Should().Equal("home.duckdns.org");
    }

    [Fact]
    public async Task Load_CloudflareProvider_BindsCorrectly()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: cloudflare
                apiToken: unused
                zones:
                  - zoneId: abc123
                    ttl: 1
                    proxied: true
                    records:
                      - home.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        var provider = options.Providers[0].Should().BeOfType<CloudflareProviderConfig>().Subject;
        provider.Zones[0].ZoneId.Should().Be("abc123");
        provider.Zones[0].Proxied.Should().BeTrue();
        provider.Zones[0].Records.Should().Equal("home.example.com");
    }

    [Fact]
    public async Task Load_NoIpProvider_BindsCorrectly()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: noip
                username: test-user
                password: test-pass
                records:
                  - home.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        var provider = options.Providers[0].Should().BeOfType<NoIpProviderConfig>().Subject;
        provider.Username.Should().Be("test-user");
        provider.Password.Should().Be("test-pass");
        provider.Records.Should().Equal("home.example.com");
    }

    [Fact]
    public async Task Load_MiabProvider_BindsCorrectly()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: miab
                hostname: box.example.com
                username: admin@example.com
                password: test-pass
                records:
                  - home.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        var provider = options.Providers[0].Should().BeOfType<MiabProviderConfig>().Subject;
        provider.Hostname.Should().Be("box.example.com");
        provider.Username.Should().Be("admin@example.com");
        provider.Password.Should().Be("test-pass");
        provider.Records.Should().Equal("home.example.com");
    }

    [Fact]
    public async Task Load_EnableIpv6_DefaultsToFalse()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        options.EnableIpv6.Should().BeFalse();
    }

    [Fact]
    public async Task Load_EnableIpv6_BindsCorrectly()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            enableIpv6: true
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        options.EnableIpv6.Should().BeTrue();
    }

    [Fact]
    public async Task Load_PublicIpSources_BindsCorrectly()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            publicIpSources:
              - ipify
              - icanhazip
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        options.PublicIpSources.Should().Equal("ipify", "icanhazip");
    }

    [Fact]
    public async Task Load_UnknownPublicIpSource_ThrowsNamingSource()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            publicIpSources:
              - carrier-pigeon
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
            """);

        var act = () => CreateSut().Load(ConfigPath, envPath: null);

        act.Should().Throw<ConfigValidationException>()
            .WithMessage("*carrier-pigeon*");
    }

    [Fact]
    public async Task Load_AaaaSuffixedRecord_BindsRawStringUnchanged()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            enableIpv6: true
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - vpn.example.com:aaaa
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        var provider = options.Providers[0].Should().BeOfType<Route53ProviderConfig>().Subject;
        provider.Zones[0].Records.Should().Equal("vpn.example.com:aaaa");
    }

    [Fact]
    public async Task Load_UnknownRecordTypeSuffix_ThrowsNamingSuffix()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - vpn.example.com:cname
            """);

        var act = () => CreateSut().Load(ConfigPath, envPath: null);

        act.Should().Throw<ConfigValidationException>()
            .WithMessage("*vpn.example.com:cname*");
    }

    [Fact]
    public async Task Load_PruneRemovedRecords_DefaultsToFalseForRoute53()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        var provider = options.Providers[0].Should().BeOfType<Route53ProviderConfig>().Subject;
        provider.PruneRemovedRecords.Should().BeFalse();
    }

    [Fact]
    public async Task Load_PruneRemovedRecords_BindsCorrectlyForRoute53CloudflareAndMiab()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: route53
                accessKeyId: unused
                secretAccessKey: unused
                pruneRemovedRecords: true
                zones:
                  - hostedZoneId: Z123
                    ttl: 300
                    records:
                      - a.example.com
              - type: cloudflare
                apiToken: unused
                pruneRemovedRecords: true
                zones:
                  - zoneId: abc123
                    ttl: 1
                    records:
                      - b.example.com
              - type: miab
                hostname: box.example.com
                username: admin@example.com
                password: test-pass
                pruneRemovedRecords: true
                records:
                  - c.example.com
            """);

        var options = CreateSut().Load(ConfigPath, envPath: null);

        options.Providers[0].Should().BeOfType<Route53ProviderConfig>().Subject.PruneRemovedRecords.Should().BeTrue();
        options.Providers[1].Should().BeOfType<CloudflareProviderConfig>().Subject.PruneRemovedRecords.Should().BeTrue();
        options.Providers[2].Should().BeOfType<MiabProviderConfig>().Subject.PruneRemovedRecords.Should().BeTrue();
    }

    [Fact]
    public async Task Load_PruneRemovedRecordsOnDuckDns_ThrowsSinceDuckDnsCannotDeleteRecords()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: duckdns
                token: test-token
                pruneRemovedRecords: true
                records:
                  - home.duckdns.org
            """);

        var act = () => CreateSut().Load(ConfigPath, envPath: null);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public async Task Load_UnknownProviderType_ThrowsNamingType()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: godaddy
                apiToken: unused
            """);

        var act = () => CreateSut().Load(ConfigPath, envPath: null);

        act.Should().Throw<ConfigValidationException>()
            .WithMessage("*godaddy*");
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
