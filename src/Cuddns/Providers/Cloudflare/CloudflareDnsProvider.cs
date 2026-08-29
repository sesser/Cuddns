using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Cuddns.Providers.Cloudflare;

public sealed class CloudflareDnsProvider : IDnsProvider
{
    private const string ApiBaseUrl = "https://api.cloudflare.com/client/v4";

    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly Dictionary<string, CloudflareZoneConfig> _zoneByRecord;

    public CloudflareDnsProvider(HttpClient httpClient, CloudflareProviderConfig config)
    {
        _httpClient = httpClient;
        _apiToken = config.ApiToken!;
        _zoneByRecord = config.Zones
            .SelectMany(zone => zone.Records.Select(record => (Record: record, Zone: zone)))
            .ToDictionary(x => x.Record, x => x.Zone);
        ManagedRecords = config.Zones
            .SelectMany(zone => zone.Records.Select(record => new ManagedRecord(record, zone.Ttl)))
            .ToList();
    }

    public string ProviderType => "cloudflare";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public async Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken)
    {
        var zone = _zoneByRecord[record.Name];
        var existingRecordId = await FindExistingRecordIdAsync(zone.ZoneId, record.Name, cancellationToken);

        var body = new CloudflareDnsRecordRequest("A", record.Name, ip, zone.Ttl, zone.Proxied);

        using var request = existingRecordId is null
            ? new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/zones/{zone.ZoneId}/dns_records")
            : new HttpRequestMessage(HttpMethod.Put, $"{ApiBaseUrl}/zones/{zone.ZoneId}/dns_records/{existingRecordId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Content = JsonContent.Create(body);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<CloudflareWriteResponse>(cancellationToken);

        if (result is null || !result.Success)
        {
            throw new InvalidOperationException(
                $"Cloudflare update for {record.Name} failed: {DescribeErrors(result?.Errors, response)}");
        }
    }

    private async Task<string?> FindExistingRecordIdAsync(string zoneId, string recordName, CancellationToken cancellationToken)
    {
        var url = $"{ApiBaseUrl}/zones/{zoneId}/dns_records?type=A&name={Uri.EscapeDataString(recordName)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<CloudflareListResponse>(cancellationToken);

        if (result is null || !result.Success)
        {
            throw new InvalidOperationException(
                $"Cloudflare lookup for {recordName} failed: {DescribeErrors(result?.Errors, response)}");
        }

        return result.Result?.FirstOrDefault()?.Id;
    }

    private static string DescribeErrors(List<CloudflareApiError>? errors, HttpResponseMessage response) =>
        errors is { Count: > 0 } ? string.Join("; ", errors.Select(e => e.Message)) : $"HTTP {(int)response.StatusCode}";
}
