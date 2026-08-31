using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Cuddns.Providers.Cloudflare;

public sealed class CloudflareDnsProvider : IDnsProvider
{
    private const string ApiBaseUrl = "https://api.cloudflare.com/client/v4";

    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly Dictionary<ManagedRecord, CloudflareZoneConfig> _zoneByRecord;

    public CloudflareDnsProvider(HttpClient httpClient, CloudflareProviderConfig config)
    {
        _httpClient = httpClient;
        _apiToken = config.ApiToken!;
        var parsedRecords = config.Zones
            .SelectMany(zone => zone.Records.Select(record => (Spec: RecordSpec.Parse(record), Zone: zone)))
            .Select(x => (Record: new ManagedRecord(x.Spec.Name, x.Zone.Ttl, x.Spec.Type), x.Zone))
            .ToList();
        _zoneByRecord = parsedRecords.ToDictionary(x => x.Record, x => x.Zone);
        ManagedRecords = parsedRecords.Select(x => x.Record).ToList();
    }

    public string ProviderType => "cloudflare";

    public IReadOnlyList<ManagedRecord> ManagedRecords { get; }

    public async Task UpsertRecordAsync(ManagedRecord record, string ip, CancellationToken cancellationToken)
    {
        var zone = _zoneByRecord[record];
        var recordTypeName = record.Type.ToString();
        var existingRecordId = await FindExistingRecordIdAsync(zone.ZoneId, record.Name, recordTypeName, cancellationToken);

        var body = new CloudflareDnsRecordRequest(recordTypeName, record.Name, ip, zone.Ttl, zone.Proxied);

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

    private async Task<string?> FindExistingRecordIdAsync(
        string zoneId, string recordName, string recordType, CancellationToken cancellationToken)
    {
        var url = $"{ApiBaseUrl}/zones/{zoneId}/dns_records?type={recordType}&name={Uri.EscapeDataString(recordName)}";
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
