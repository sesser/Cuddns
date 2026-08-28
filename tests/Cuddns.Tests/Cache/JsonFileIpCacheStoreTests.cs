using Cuddns.Cache;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cuddns.Tests.Cache;

public class JsonFileIpCacheStoreTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("cuddns-tests-").FullName;

    private string CachePath => Path.Combine(_tempDir, "cache.json");

    private JsonFileIpCacheStore CreateSut() =>
        new(CachePath, NullLogger<JsonFileIpCacheStore>.Instance);

    [Fact]
    public async Task SaveThenLoad_RoundTripsEntriesExactly()
    {
        var sut = CreateSut();
        var entries = new Dictionary<string, IpCacheEntry>
        {
            ["a.example.com"] = new IpCacheEntry("203.0.113.10", DateTimeOffset.UtcNow),
            ["b.example.com"] = new IpCacheEntry("203.0.113.11", DateTimeOffset.UtcNow.AddMinutes(-5)),
        };

        await sut.SaveAsync(entries, CancellationToken.None);
        var loaded = await sut.LoadAsync(CancellationToken.None);

        loaded.Should().BeEquivalentTo(entries);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyDictionary()
    {
        var sut = CreateSut();

        var loaded = await sut.LoadAsync(CancellationToken.None);

        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsEmptyDictionaryAndDoesNotThrow()
    {
        await File.WriteAllTextAsync(CachePath, "{ not valid json ][");
        var sut = CreateSut();

        var loaded = await sut.LoadAsync(CancellationToken.None);

        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_TwiceInARow_LeavesValidJsonReflectingLatestSave()
    {
        var sut = CreateSut();
        await sut.SaveAsync(new Dictionary<string, IpCacheEntry>
        {
            ["a.example.com"] = new IpCacheEntry("1.1.1.1", DateTimeOffset.UtcNow),
        }, CancellationToken.None);

        var secondEntries = new Dictionary<string, IpCacheEntry>
        {
            ["a.example.com"] = new IpCacheEntry("2.2.2.2", DateTimeOffset.UtcNow),
        };
        await sut.SaveAsync(secondEntries, CancellationToken.None);

        var loaded = await sut.LoadAsync(CancellationToken.None);

        loaded.Should().BeEquivalentTo(secondEntries);
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Save_CreatesCacheDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "dir", "cache.json");
        var sut = new JsonFileIpCacheStore(nestedPath, NullLogger<JsonFileIpCacheStore>.Instance);

        await sut.SaveAsync(new Dictionary<string, IpCacheEntry>(), CancellationToken.None);

        File.Exists(nestedPath).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
