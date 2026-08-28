using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Cuddns.Cache;

public sealed class JsonFileIpCacheStore(string cachePath, ILogger<JsonFileIpCacheStore> logger) : IIpCacheStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyDictionary<string, IpCacheEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
        {
            return new Dictionary<string, IpCacheEntry>();
        }

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, IpCacheEntry>>(
                stream, SerializerOptions, cancellationToken);
            return entries ?? new Dictionary<string, IpCacheEntry>();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "IP cache file at {CachePath} is corrupt; treating cache as empty.", cachePath);
            return new Dictionary<string, IpCacheEntry>();
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, IpCacheEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(cachePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, entries, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, cachePath, overwrite: true);
    }
}
