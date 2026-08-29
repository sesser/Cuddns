namespace Cuddns.Providers.DuckDns;

public sealed class DuckDnsProviderFactory : IDnsProviderFactory
{
    // Shared across every DuckDNS provider instance this process creates, per standard
    // HttpClient guidance (avoids socket exhaustion from creating one per Create() call).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string ProviderType => "duckdns";

    public Type ConfigType => typeof(DuckDnsProviderConfig);

    public IProviderConfig CreateDefaultConfig() => new DuckDnsProviderConfig();

    public IDnsProvider Create(IProviderConfig config) => new DuckDnsProvider(HttpClient, (DuckDnsProviderConfig)config);
}
