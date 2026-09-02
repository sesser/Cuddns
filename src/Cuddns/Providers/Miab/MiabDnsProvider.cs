using System.Net.Http.Headers;
using System.Text;

namespace Cuddns.Providers.Miab;

public sealed class MiabDnsProvider : IDnsProvider
{
    // MiaB's custom DNS API doesn't expose a TTL knob; this is informational only, same as
    // DuckDNS/No-IP.
    private const int MiabTtlSeconds = 300;

    private readonly HttpClient _httpClient;
    private readonly string _hostname;
    private readonly AuthenticationHeaderValue _authHeader;

    public MiabDnsProvider(HttpClient httpClient, MiabProviderConfig config)
    {
        _httpClient = httpClient;
        _hostname = config.Hostname!;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
        _authHeader = new AuthenticationHeaderValue("Basic", credentials);
        ManagedRecords = config.Records.Select(record =>
        {
            var (name, type) = RecordSpec.Parse(record);
            return new ManagedRecord(name, MiabTtlSeconds, type);
        }).ToList();
    }

    public string ProviderType => "miab";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public async Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken)
    {
        var rtype = record.Type == RecordType.AAAA ? "aaaa" : "a";
        var url = $"https://{_hostname}/admin/dns/custom/{Uri.EscapeDataString(record.Name)}/{rtype}";

        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            // The API defaults A/AAAA to the caller's own remote address when the request
            // body is empty — send the IP Cuddns resolved explicitly instead, since the box
            // isn't necessarily what sees the same public address Cuddns did.
            Content = new StringContent(ip),
        };
        request.Headers.Authorization = _authHeader;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

        if (!response.IsSuccessStatusCode || !result.Equals("OK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"MiaB update for {record.Name} failed: '{Truncate(result)}'");
        }
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "...";
}
