using Microsoft.Extensions.Logging;

namespace Cuddns.Providers.NoIp;

public sealed class NoIpProviderFactory(ILogger<NoIpProviderFactory> logger) : IDnsProviderFactory
{
    // Shared across every No-IP provider instance this process creates, per standard
    // HttpClient guidance (avoids socket exhaustion from creating one per Create() call).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string ProviderType => "noip";

    public Type ConfigType => typeof(NoIpProviderConfig);

    public IProviderConfig CreateDefaultConfig() => new NoIpProviderConfig();

    public IDnsProvider Create(IProviderConfig config)
    {
        var noIpConfig = (NoIpProviderConfig)config;
        logger.LogInformation("Creating noip provider ({RecordCount} records)", noIpConfig.Records.Count);
        return new NoIpProvider(HttpClient, noIpConfig);
    }
}
