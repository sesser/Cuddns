using Microsoft.Extensions.Logging;

namespace Cuddns.Providers.Cloudflare;

public sealed class CloudflareDnsProviderFactory(ILogger<CloudflareDnsProviderFactory> logger) : IDnsProviderFactory
{
    // Shared across every Cloudflare provider instance this process creates, per standard
    // HttpClient guidance (avoids socket exhaustion from creating one per Create() call).
    // The API token varies per provider instance, so it's sent per-request rather than as a
    // default header on this shared client.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string ProviderType => "cloudflare";

    public Type ConfigType => typeof(CloudflareProviderConfig);

    public IProviderConfig CreateDefaultConfig() => new CloudflareProviderConfig();

    public IDnsProvider Create(IProviderConfig config)
    {
        var cloudflareConfig = (CloudflareProviderConfig)config;
        logger.LogInformation(
            "Creating cloudflare provider (zones: {ZoneCount}, records: {RecordCount})",
            cloudflareConfig.Zones.Count, cloudflareConfig.Zones.Sum(z => z.Records.Count));
        return new CloudflareDnsProvider(HttpClient, cloudflareConfig);
    }
}
