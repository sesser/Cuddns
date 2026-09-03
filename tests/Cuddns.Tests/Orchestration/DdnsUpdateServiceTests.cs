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
    private const string CurrentIpv6 = "2001:db8::1";

    private readonly Mock<IPublicIpResolver> _publicIp = new();
    private readonly Mock<IIpCacheStore> _cacheStore = new();
    private readonly Mock<IDnsProvider> _dnsProvider = new();

    public DdnsUpdateServiceTests()
    {
        _publicIp.Setup(p => p.GetCurrentIpsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicIpResult(CurrentIp, CurrentIpv6));
    }

    private Task RunOnce(params IDnsProvider[] providers) =>
        new DdnsUpdateService(_cacheStore.Object, NullLogger<DdnsUpdateService>.Instance)
            .RunOnceAsync(providers, _publicIp.Object, CancellationToken.None);

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

        await RunOnce(_dnsProvider.Object);

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

        await RunOnce(_dnsProvider.Object);

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

        await RunOnce(_dnsProvider.Object);

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

        await RunOnce(_dnsProvider.Object);

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

        await RunOnce(_dnsProvider.Object);

        _publicIp.Verify(p => p.GetCurrentIpsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoManagedRecordsAcrossAnyProvider_SkipsPublicIpLookupButStillSavesCache()
    {
        SetupCache([]);
        SetupManagedRecords(); // no records at all — e.g. every provider was emptied out

        await RunOnce(_dnsProvider.Object);

        _publicIp.Verify(p => p.GetCurrentIpsAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cacheStore.Verify(c => c.SaveAsync(It.IsAny<IReadOnlyDictionary<string, IpCacheEntry>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AaaaRecord_UsesIPv6AndDistinctCacheKeyFromARecordWithSameName()
    {
        SetupCache([]);
        _dnsProvider.Setup(p => p.ManagedRecords).Returns(
        [
            new ManagedRecord("dual.example.com", 300, RecordType.A),
            new ManagedRecord("dual.example.com", 300, RecordType.AAAA),
        ]);

        await RunOnce(_dnsProvider.Object);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "dual.example.com" && r.Type == RecordType.A),
            CurrentIp, It.IsAny<CancellationToken>()), Times.Once);
        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "dual.example.com" && r.Type == RecordType.AAAA),
            CurrentIpv6, It.IsAny<CancellationToken>()), Times.Once);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d =>
                d["dual.example.com"].Ip == CurrentIp &&
                d["dual.example.com|AAAA"].Ip == CurrentIpv6),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AaaaRecord_NoIPv6AddressAvailable_SkipsWithoutCallingProvider()
    {
        SetupCache([]);
        _publicIp.Setup(p => p.GetCurrentIpsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicIpResult(CurrentIp, null));
        _dnsProvider.Setup(p => p.ManagedRecords).Returns(
        [
            new ManagedRecord("v6only.example.com", 300, RecordType.AAAA),
        ]);

        await RunOnce(_dnsProvider.Object);

        _dnsProvider.Verify(p => p.UpsertRecordAsync(
            It.IsAny<ManagedRecord>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MultipleProvidersInOneRun_AllProcessed()
    {
        SetupCache([]);

        var first = new Mock<IDnsProvider>();
        first.Setup(p => p.ManagedRecords).Returns([new ManagedRecord("a.example.com", 300)]);

        var second = new Mock<IDnsProvider>();
        second.Setup(p => p.ManagedRecords).Returns([new ManagedRecord("b.example.com", 300)]);

        await RunOnce(first.Object, second.Object);

        first.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "a.example.com"), CurrentIp, It.IsAny<CancellationToken>()), Times.Once);
        second.Verify(p => p.UpsertRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "b.example.com"), CurrentIp, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IDeletableDnsProvider> CreateDeletableProvider(
        string providerType, bool pruneRemovedRecords, string ownedScope, params ManagedRecord[] managedRecords)
    {
        var provider = new Mock<IDeletableDnsProvider>();
        provider.Setup(p => p.ProviderType).Returns(providerType);
        provider.Setup(p => p.PruneRemovedRecords).Returns(pruneRemovedRecords);
        provider.Setup(p => p.ManagedRecords).Returns(managedRecords);
        provider.Setup(p => p.OwnsScope(ownedScope)).Returns(true);
        return provider;
    }

    [Fact]
    public async Task RemovedRecord_PruneRemovedRecordsTrue_DeletesAndPrunesCache()
    {
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["gone.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
            ["still.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
        });
        // At least one record must remain active so the empty-config guardrail (see
        // EmptyActiveConfigWithNonEmptyCache_SkipsReconciliationEntirely) doesn't kick in —
        // this mirrors the realistic case of one record among several being removed.
        var provider = CreateDeletableProvider(
            "route53", pruneRemovedRecords: true, ownedScope: "Z1", new ManagedRecord("still.example.com", 300));

        await RunOnce(provider.Object);

        provider.Verify(p => p.DeleteRecordAsync(
            It.Is<ManagedRecord>(r => r.Name == "gone.example.com"), "Z1", CurrentIp, It.IsAny<CancellationToken>()), Times.Once);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d => !d.ContainsKey("gone.example.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovedRecord_PruneRemovedRecordsFalse_LeavesRecordButPrunesCache()
    {
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["gone.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
            ["still.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
        });
        var provider = CreateDeletableProvider(
            "route53", pruneRemovedRecords: false, ownedScope: "Z1", new ManagedRecord("still.example.com", 300));

        await RunOnce(provider.Object);

        provider.Verify(p => p.DeleteRecordAsync(
            It.IsAny<ManagedRecord>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d => !d.ContainsKey("gone.example.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovedRecord_DeleteThrows_CacheEntryRetainedForRetry()
    {
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["gone.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
            ["still.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
        });
        var provider = CreateDeletableProvider(
            "route53", pruneRemovedRecords: true, ownedScope: "Z1", new ManagedRecord("still.example.com", 300));
        provider.Setup(p => p.DeleteRecordAsync(
                It.IsAny<ManagedRecord>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await RunOnce(provider.Object);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d =>
                d.ContainsKey("gone.example.com") && d["gone.example.com"].Ip == CurrentIp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovedRecord_NoOwningProviderConfigured_PrunesCacheWithoutDeleting()
    {
        // The whole zone/provider that used to own this record is gone from config, so no
        // configured instance recognizes the scope — nothing left to call delete with.
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["gone.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
            ["still.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z2"),
        });
        var stillAround = CreateDeletableProvider(
            "route53", pruneRemovedRecords: true, ownedScope: "Z2", new ManagedRecord("still.example.com", 300));

        await RunOnce(stillAround.Object);

        stillAround.Verify(p => p.DeleteRecordAsync(
            It.IsAny<ManagedRecord>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d => !d.ContainsKey("gone.example.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyActiveConfigWithNonEmptyCache_SkipsReconciliationEntirely()
    {
        SetupCache(new Dictionary<string, IpCacheEntry>
        {
            ["gone.example.com"] = new IpCacheEntry(CurrentIp, DateTimeOffset.UtcNow.AddHours(-1), "route53", "Z1"),
        });

        await RunOnce();

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d => d.ContainsKey("gone.example.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertRecordAsync_OnDeletableProvider_PersistsProviderTypeAndScopeInCache()
    {
        SetupCache([]);
        var provider = CreateDeletableProvider(
            "route53", pruneRemovedRecords: false, ownedScope: "Z1", new ManagedRecord("a.example.com", 300));
        provider.Setup(p => p.GetScope(It.IsAny<ManagedRecord>())).Returns("Z1");

        await RunOnce(provider.Object);

        _cacheStore.Verify(c => c.SaveAsync(
            It.Is<IReadOnlyDictionary<string, IpCacheEntry>>(d =>
                d["a.example.com"].ProviderType == "route53" && d["a.example.com"].Scope == "Z1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
