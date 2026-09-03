using Cuddns.Config;
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

public class ConfigWatcherServiceTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("cuddns-watcher-tests-").FullName;

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
    private string EnvPath => Path.Combine(_tempDir, ".env");

    private const string ValidConfigWithOneRecord = """
        intervalSeconds: 60
        providers:
          - type: duckdns
            token: test-token
            records:
              - home.duckdns.org
        """;

    private const string ValidConfigWithTwoRecords = """
        intervalSeconds: 60
        providers:
          - type: duckdns
            token: test-token
            records:
              - home.duckdns.org
              - vpn.duckdns.org
        """;

    private ConfigSnapshotBuilder CreateSnapshotBuilder() => new(
        new ConfigLoader(_catalogByType.Values.ToList()),
        _catalogByType,
        _publicIpSourceCatalog,
        NullLogger<PublicIpResolver>.Instance);

    private async Task<(ConfigWatcherService Watcher, ConfigState State)> CreateSutAsync(bool withEnvFile = false)
    {
        await File.WriteAllTextAsync(ConfigPath, ValidConfigWithOneRecord);
        if (withEnvFile)
        {
            await File.WriteAllTextAsync(EnvPath, "SOME_VAR=original");
        }

        var builder = CreateSnapshotBuilder();
        var state = new ConfigState(builder.Build(ConfigPath, withEnvFile ? EnvPath : null));
        var watcher = new ConfigWatcherService(
            builder, state, ConfigPath, withEnvFile ? EnvPath : null, NullLogger<ConfigWatcherService>.Instance);
        return (watcher, state);
    }

    [Fact]
    public async Task CheckAndReloadOnceAsync_NoChange_ReturnsFalseAndKeepsSnapshot()
    {
        var (watcher, state) = await CreateSutAsync();
        var initialSnapshot = state.Current;

        var reloaded = await watcher.CheckAndReloadOnceAsync(CancellationToken.None);

        reloaded.Should().BeFalse();
        state.Current.Should().BeSameAs(initialSnapshot);
    }

    [Fact]
    public async Task CheckAndReloadOnceAsync_ValidConfigChange_SwapsSnapshotAndReturnsTrue()
    {
        var (watcher, state) = await CreateSutAsync();
        var initialSnapshot = state.Current;

        await File.WriteAllTextAsync(ConfigPath, ValidConfigWithTwoRecords);
        File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow.AddSeconds(5));

        var reloaded = await watcher.CheckAndReloadOnceAsync(CancellationToken.None);

        reloaded.Should().BeTrue();
        state.Current.Should().NotBeSameAs(initialSnapshot);
        state.Current.DnsProviders[0].ManagedRecords.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckAndReloadOnceAsync_InvalidYaml_KeepsPreviousSnapshotAndReturnsFalse()
    {
        var (watcher, state) = await CreateSutAsync();
        var initialSnapshot = state.Current;

        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: duckdns
                records:
                  - home.duckdns.org
            """); // missing required "token"
        File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow.AddSeconds(5));

        var reloaded = await watcher.CheckAndReloadOnceAsync(CancellationToken.None);

        reloaded.Should().BeFalse();
        state.Current.Should().BeSameAs(initialSnapshot);
    }

    [Fact]
    public async Task CheckAndReloadOnceAsync_NewUnresolvedEnvVar_KeepsPreviousSnapshot()
    {
        var (watcher, state) = await CreateSutAsync();
        var initialSnapshot = state.Current;

        await File.WriteAllTextAsync(ConfigPath, """
            intervalSeconds: 60
            providers:
              - type: duckdns
                token: ${CUDDNS_TEST_MISSING_VAR}
                records:
                  - home.duckdns.org
            """);
        File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow.AddSeconds(5));

        var reloaded = await watcher.CheckAndReloadOnceAsync(CancellationToken.None);

        reloaded.Should().BeFalse();
        state.Current.Should().BeSameAs(initialSnapshot);
    }

    [Fact]
    public async Task CheckAndReloadOnceAsync_ConfigFileMissingAtPollTime_ReturnsFalseWithoutThrowing()
    {
        var (watcher, state) = await CreateSutAsync();
        var initialSnapshot = state.Current;
        File.Delete(ConfigPath);

        var act = () => watcher.CheckAndReloadOnceAsync(CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.Should().BeFalse();
        state.Current.Should().BeSameAs(initialSnapshot);
    }

    [Fact]
    public async Task CheckAndReloadOnceAsync_OnlyEnvFileChanges_TriggersReloadToo()
    {
        var (watcher, state) = await CreateSutAsync(withEnvFile: true);
        var initialSnapshot = state.Current;

        await File.WriteAllTextAsync(EnvPath, "SOME_VAR=changed");
        File.SetLastWriteTimeUtc(EnvPath, DateTime.UtcNow.AddSeconds(5));
        // config.yaml itself is untouched — only .env changed.

        var reloaded = await watcher.CheckAndReloadOnceAsync(CancellationToken.None);

        reloaded.Should().BeTrue();
        state.Current.Should().NotBeSameAs(initialSnapshot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
