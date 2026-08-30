namespace Cuddns.Providers.Cloudflare;

public sealed class CloudflareZoneConfig
{
    public string ZoneId { get; set; } = string.Empty;

    public int Ttl { get; set; } = 300;

    /// <summary>Whether records are proxied through Cloudflare (orange cloud) rather than DNS-only.</summary>
    public bool Proxied { get; set; }

    public List<string> Records { get; set; } = [];
}
