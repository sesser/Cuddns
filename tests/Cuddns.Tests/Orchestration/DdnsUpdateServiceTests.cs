using Cuddns.Cache;
using Cuddns.Options;
using Cuddns.Orchestration;
using Cuddns.Providers;
using Cuddns.PublicIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cuddns.Tests.Orchestration;

public class DdnsUpdateServiceTests
{
    private const string ProviderType = "fake";
    private const string CurrentIp = "203.0.113.10";

    private readonly Mock<IPublicIpProvider> _publicIp = new();
    private readonly Mock<IIpCacheStore> _cacheStore = new();
    private readonly Mock<IDnsProvider> _dnsProvider = new();
    private readonly Mock<IDnsProviderFactory> _dnsProviderFactory = new();

    public DdnsUpdateServiceTests()
    {
        _publicIp.Setup(p => p.GetCurrentIpAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentIp);

        _dnsProviderFactory.Setup(f => f.ProviderType).Returns(ProviderType);
        _dnsProviderFactory.Setup(f => f.Create(It.IsAny<ProviderOptions>()))
            .Returns(_dnsProvider.Object);
    }

    private DdnsUpdateService CreateSut()
    {
        var factories = new Dictionary<string, IDnsProviderFactory> { [ProviderType] = _dnsProviderFactory.Object };
        return new DdnsUpdateService(
            _publicIp.Object,
            _cacheStore.Object,
            factories,
            NullLogger<DdnsUpdateService>.Instance);
    }

    private static CuddnsOptions BuildConfig(params (string ZoneId, string[] Records)[] zones)
    {
        return new CuddnsOptions
        {
            IntervalSeconds = 60,
            Providers =
            [
                new ProviderOptions
                {
                    Type = ProviderType,
                    Zones = zones.Select(z => new ZoneOptions
                    {
                        HostedZoneId = z.ZoneId,
                        Ttl = 300,
                        Records = [.. z.Records],
                    }).ToList(),
                },
            ],
        };
    }

    private void SetupCache(Dictionary<string, IpCacheEntry> initial)
    {
        _cacheStore.Setup(c => c.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, IpCacheEntry>)initial);
    }

    [Fact]
    public async Task FirstRun_NoCacheEntry_CallsProviderAndSavesCache()
    {
        SetupCache([]);
        var config = BuildConfig(("Z1", ["a.example.com"]));

        await CreateSut().RunOnceAsync(config, CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ZoneOptions>(), "a.example.com", CurrentIp, It.IsAny<CancellationToken>()), Times.Once);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d =>
                d.ContainsKey("a.example.com") && d["a.example.com"].Ip == CurrentIp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CachedIpMatchesCurrentIp_SkipsProviderCall()
    {
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["a.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1)),
        });
        var config = BuildConfig(("Z1", ["a.example.com"]));

        await CreateSut().RunOnceAsync(config, CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ZoneOptions>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CachedIpDiffersFromCurrentIp_CallsProviderAndUpdatesCache()
    {
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["a.example.com"] = new IpCacheEntry("198.51.100.1", DateTimeOffset.UtcNow.AddHours(-1)),
        });
        var config = BuildConfig(("Z1", ["a.example.com"]));

        await CreateSut().RunOnceAsync(config, CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ZoneOptions>(), "a.example.com", CurrentIp, It.IsAny<CancellationToken>()), Times.Once);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d => d["a.example.com"].Ip == CurrentIp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderThrows_OtherRecordsStillProcessed_FailedRecordCacheNotAdvanced()
    {
        SetupCache([]);
        var config = BuildConfig(("Z1", ["fails.example.com", "ok.example.com"]));

        _dnsProvider.Setup(p => p.UpsertRecordAsync(
                It.IsAny<ZoneOptions>(), "fails.example.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await CreateSut().RunOnceAsync(config, CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ZoneOptions>(), "ok.example.com", CurrentIp, It.IsAny<CancellationToken>()), Times.Once);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d =>
                !d.ContainsKey("fails.example.com") && d["ok.example.com"].Ip == CurrentIp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicIpFetchedExactlyOnce_RegardlessOfRecordCount()
    {
        SetupCache([]);
        var config = BuildConfig(("Z1", ["a.example.com", "b.example.com", "c.example.com"]));

        await CreateSut().RunOnceAsync(config, CancellationToken.None);

        _publicIp.Verify(p => p.GetCurrentIpAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MultipleZonesInOneProvider_AllProcessedInSingleRun()
    {
        SetupCache([]);
        var config = BuildConfig(
            ("Z1", ["a.example.com"]),
            ("Z2", ["b.example.com"]));

        await CreateSut().RunOnceAsync(config, CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ZoneOptions>(), "a.example.com", CurrentIp, It.IsAny<CancellationToken>()), Times.Once);
        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ZoneOptions>(), "b.example.com", CurrentIp, It.IsAny<CancellationToken>()), Times.Once);
    }
}
