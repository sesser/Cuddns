namespace Cuddns.PublicIp;

public sealed class IpifyPublicIpSource(HttpClient httpClient) : FamilyPinnedHttpPublicIpSource(httpClient)
{
    public override string Name => "ipify";

    protected override string GetUrl(IpFamily family) =>
        family == IpFamily.IPv4 ? "https://api.ipify.org" : "https://api6.ipify.org";
}
