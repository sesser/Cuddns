namespace Cuddns.Options;

public sealed class ProviderOptions
{
    public string Type { get; set; } = string.Empty;

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? Region { get; set; }

    public List<ZoneOptions> Zones { get; set; } = [];
}
