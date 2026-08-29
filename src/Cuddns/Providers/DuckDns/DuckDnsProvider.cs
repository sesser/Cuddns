namespace Cuddns.Providers.DuckDns;

public sealed class DuckDnsProvider : IDnsProvider
{
    // DuckDNS manages TTL itself (advertised as 60s); not configurable via its update API.
    private const int DuckDnsTtlSeconds = 60;

    private readonly HttpClient _httpClient;
    private readonly string _token;

    public DuckDnsProvider(HttpClient httpClient, DuckDnsProviderConfig config)
    {
        _httpClient = httpClient;
        _token = config.Token!;
        ManagedRecords = config.Records.Select(record => new ManagedRecord(record, DuckDnsTtlSeconds)).ToList();
    }

    public string ProviderType => "duckdns";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public async Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken)
    {
        var subdomain = record.Name[..^".duckdns.org".Length];
        var url = $"https://www.duckdns.org/update?domains={Uri.EscapeDataString(subdomain)}" +
                  $"&token={Uri.EscapeDataString(_token)}&ip={Uri.EscapeDataString(ip)}";

        var response = await _httpClient.GetStringAsync(url, cancellationToken);
        var result = response.Trim();

        if (!result.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"DuckDNS update for {record.Name} failed: '{Truncate(result)}'");
        }
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "...";
}
