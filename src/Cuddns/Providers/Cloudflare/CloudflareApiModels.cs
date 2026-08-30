using System.Text.Json.Serialization;

namespace Cuddns.Providers.Cloudflare;

internal sealed record CloudflareDnsRecordRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("ttl")] int Ttl,
    [property: JsonPropertyName("proxied")] bool Proxied);

internal sealed record CloudflareDnsRecord([property: JsonPropertyName("id")] string Id);

internal sealed record CloudflareApiError([property: JsonPropertyName("message")] string Message);

internal sealed record CloudflareListResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("result")] List<CloudflareDnsRecord>? Result,
    [property: JsonPropertyName("errors")] List<CloudflareApiError>? Errors);

internal sealed record CloudflareWriteResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("errors")] List<CloudflareApiError>? Errors);
