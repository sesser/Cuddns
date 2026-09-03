using Cuddns.Config;
using Cuddns.Options;
using Cuddns.Providers;
using Cuddns.Providers.Cloudflare;
using Cuddns.Providers.DuckDns;
using Cuddns.Providers.Miab;
using Cuddns.Providers.NoIp;
using Cuddns.Providers.Route53;
using Cuddns.PublicIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cuddns.Tests.Config;

public class ConfigSnapshotBuilderTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("cuddns-snapshot-builder-tests-").FullName;

    private readonly IReadOnlyDictionary<string, IDnsProviderFactory> _catalogByType =
        new IDnsProviderFactory[]
        {
            new Route53DnsProviderFactory(NullLogger<Route53DnsProviderFactory>.Instance),
            new DuckDnsProviderFactory(NullLogger<DuckDnsProviderFactory>.Instance),
            new CloudflareDnsProviderFactory(NullLogger<CloudflareDnsProviderFactory>.Instance),
            new NoIpProviderFactory(NullLogger<NoIpProviderFactory>.Instance),
            new MiabDnsProviderFactory(NullLogger<MiabDnsProviderFactory>.Instance),
        }.ToDictionary(f => f.ProviderType);

    private readonly IReadOnlyDictionary<string, IPublicIpSource> _publicIpSourceCatalog =
        new Dictionary<string, IPublicIpSource>
        {
            [PublicIpSourceNames.IfConfig] = Mock.Of<IPublicIpSource>(),
            [PublicIpSourceNames.Ipify] = Mock.Of<IPublicIpSource>(),
            [PublicIpSourceNames.Icanhazip] = Mock.Of<IPublicIpSource>(),
            [PublicIpSourceNames.IdentMe] = Mock.Of<IPublicIpSource>(),
        };

    private string ConfigPath => Path.Combine(_tempDir, "config.yaml");

    private ConfigSnapshotBuilder CreateSut() => new(
        new ConfigLoader(_catalogByType.Values.ToList()),
        _catalogByType,
        _publicIpSourceCatalog,
        NullLogger<PublicIpResolver>.Instance);

    [Fact]
    public async Task Build_ValidConfig_ReturnsSnapshotWithProvidersAndResolver()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: duckdns
                token: test-token
                records:
                  - home.duckdns.org
            """);

        var snapshot = CreateSut().Build(ConfigPath, envPath: null);

        snapshot.Options.Providers.Should().ContainSingle();
        snapshot.DnsProviders.Should().ContainSingle();
        snapshot.DnsProviders[0].ManagedRecords.Should().ContainSingle(r => r.Name == "home.duckdns.org");
        snapshot.PublicIpResolver.Should().NotBeNull();
    }

    [Fact]
    public async Task Build_InvalidConfig_PropagatesConfigValidationException()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: duckdns
                records:
                  - home.duckdns.org
            """);

        var act = () => CreateSut().Build(ConfigPath, envPath: null);

        act.Should().Throw<ConfigValidationException>().WithMessage("*token*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
