namespace Cuddns.Options;

public sealed class ZoneOptions
{
    public string HostedZoneId { get; set; } = string.Empty;

    public int Ttl { get; set; } = 300;

    public List<string> Records { get; set; } = [];
}
