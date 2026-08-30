namespace Cuddns.PublicIp;

public sealed class IcanhazipPublicIpSource(HttpClient httpClient) : FamilyPinnedHttpPublicIpSource(httpClient)
{
    public override string Name => "icanhazip";

    protected override string GetUrl(IpFamily family) =>
        family == IpFamily.IPv4 ? "https://ipv4.icanhazip.com" : "https://ipv6.icanhazip.com";
}
