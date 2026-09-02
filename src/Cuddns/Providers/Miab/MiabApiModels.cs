using System.Text.Json.Serialization;

namespace Cuddns.Providers.Miab;

internal sealed record MiabLoginResponse(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("api_key")] string? ApiKey);
