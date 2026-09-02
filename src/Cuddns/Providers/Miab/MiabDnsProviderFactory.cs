using Microsoft.Extensions.Logging;

namespace Cuddns.Providers.Miab;

public sealed class MiabDnsProviderFactory(ILogger<MiabDnsProviderFactory> logger) : IDnsProviderFactory
{
    // Shared across every MiaB provider instance this process creates, per standard
    // HttpClient guidance (avoids socket exhaustion from creating one per Create() call).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string ProviderType => "miab";

    public Type ConfigType => typeof(MiabProviderConfig);

    public IProviderConfig CreateDefaultConfig() => new MiabProviderConfig();

    public IDnsProvider Create(IProviderConfig config)
    {
        var miabConfig = (MiabProviderConfig)config;
        logger.LogInformation("Creating miab provider ({RecordCount} records)", miabConfig.Records.Count);
        return new MiabDnsProvider(HttpClient, miabConfig);
    }
}
