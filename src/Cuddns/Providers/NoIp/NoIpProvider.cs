using System.Net.Http.Headers;
using System.Text;

namespace Cuddns.Providers.NoIp;

public sealed class NoIpProvider : IDnsProvider
{
    // No-IP doesn't expose TTL through the update API (paid plans manage it via the
    // dashboard); this is informational only, same as DuckDNS.
    private const int NoIpTtlSeconds = 300;

    private static readonly string[] SuccessCodes = ["good", "nochg"];

    private readonly HttpClient _httpClient;
    private readonly AuthenticationHeaderValue _authHeader;

    public NoIpProvider(HttpClient httpClient, NoIpProviderConfig config)
    {
        _httpClient = httpClient;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
        _authHeader = new AuthenticationHeaderValue("Basic", credentials);
        ManagedRecords = config.Records.Select(record =>
        {
            var (name, type) = RecordSpec.Parse(record);
            return new ManagedRecord(name, NoIpTtlSeconds, type);
        }).ToList();
    }

    public string ProviderType => "noip";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public async Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken)
    {
        var url = $"https://dynupdate.no-ip.com/nic/update?hostname={Uri.EscapeDataString(record.Name)}" +
                  $"&myip={Uri.EscapeDataString(ip)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = _authHeader;
        // No-IP rate-limits/blocks clients that send a missing or generic User-Agent.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Cuddns", AppVersion.Current));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/sesser/Cuddns)"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        var code = result.Split(' ', 2)[0];

        if (!SuccessCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"No-IP update for {record.Name} failed: '{Truncate(result)}'");
        }
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "...";
}
