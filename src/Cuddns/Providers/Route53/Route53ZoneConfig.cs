namespace Cuddns.Providers.Route53;

public sealed class Route53ZoneConfig
{
    public string HostedZoneId { get; set; } = string.Empty;

    public int Ttl { get; set; } = 300;

    public List<string> Records { get; set; } = [];
}
