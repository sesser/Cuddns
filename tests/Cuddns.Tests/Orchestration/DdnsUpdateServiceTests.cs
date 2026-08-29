using Cuddns.Cache;
using Cuddns.Orchestration;
using Cuddns.Providers;
using Cuddns.PublicIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Cuddns.Tests.Orchestration;

public class DdnsUpdateServiceTests
{
    private const string CurrentIp = "203.0.113.10";

    private readonly Mock<IPublicIpProvider> _publicIp = new();
    private readonly Mock<IIpCacheStore> _cacheStore = new();
    private readonly Mock<IDnsProvider> _dnsProvider = new();

    public DdnsUpdateServiceTests()
    {
        _publicIp.Setup(p => p.GetCurrentIpAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentIp);
    }

    private DdnsUpdateService CreateSut(params IDnsProvider[] providers)
    {
        return new DdnsUpdateService(
            _publicIp.Object,
            _cacheStore.Object,
            providers,
            NullLogger<DdnsUpdateService>.Instance);
    }

    private void SetupManagedRecords(params string[] records)
    {
        _dnsProvider.Setup(p => p.ManagedRecords)
            .Returns(records.Select(r => new ManagedRecord(r, 300)).ToList());
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
        SetupManagedRecords("a.example.com");

        await CreateSut(_dnsProvider.Object).RunOnceAsync(CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "a.example.com"), CurrentIp, It.IsAny<CancellationToken>()), Times.Once);

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
        SetupManagedRecords("a.example.com");

        await CreateSut(_dnsProvider.Object).RunOnceAsync(CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ManagedRecord>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CachedIpDiffersFromCurrentIp_CallsProviderAndUpdatesCache()
    {
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["a.example.com"] = new IpCacheEntry("198.51.100.1", DateTimeOffset.UtcNow.AddHours(-1)),
        });
        SetupManagedRecords("a.example.com");

        await CreateSut(_dnsProvider.Object).RunOnceAsync(CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "a.example.com"), CurrentIp, It.IsAny<CancellationToken>()), Times.Once);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d => d["a.example.com"].Ip == CurrentIp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderThrows_OtherRecordsStillProcessed_FailedRecordCacheNotAdvanced()
    {
        SetupCache([]);
        SetupManagedRecords("fails.example.com", "ok.example.com");

        _dnsProvider.Setup(p => p.UpsertRecordAsync(
                It.Is<ManagedRecord>(r => r.Name == "fails.example.com"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await CreateSut(_dnsProvider.Object).RunOnceAsync(CancellationToken.None);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "ok.example.com"), CurrentIp, It.IsAny<CancellationToken>()), Times.Once);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d =>
                !d.ContainsKey("fails.example.com") && d["ok.example.com"].Ip == CurrentIp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicIpFetchedExactlyOnce_RegardlessOfRecordCount()
    {
        SetupCache([]);
        SetupManagedRecords("a.example.com", "b.example.com", "c.example.com");

        await CreateSut(_dnsProvider.Object).RunOnceAsync(CancellationToken.None);

        _publicIp.Verify(p => p.GetCurrentIpAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MultipleProvidersInOneRun_AllProcessed()
    {
        SetupCache([]);

        var first = new Mock<IDnsProvider>();
        first.Setup(p => p.ManagedRecords).Returns([new ManagedRecord("a.example.com", 300)]);

        var second = new Mock<IDnsProvider>();
        second.Setup(p => p.ManagedRecords).Returns([new ManagedRecord("b.example.com", 300)]);

        await CreateSut(first.Object, second.Object).RunOnceAsync(CancellationToken.None);

        first.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "a.example.com"), CurrentIp, It.IsAny<CancellationToken>()), Times.Once);
        second.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "b.example.com"), CurrentIp, It.IsAny<CancellationToken>()), Times.Once);
    }
}
