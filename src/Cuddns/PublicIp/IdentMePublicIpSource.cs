namespace Cuddns.PublicIp;

public sealed class IdentMePublicIpSource(HttpClient httpClient) : FamilyPinnedHttpPublicIpSource(httpClient)
{
    public override string Name => "identme";

    protected override string GetUrl(IpFamily family) =>
        family == IpFamily.IPv4 ? "https://v4.ident.me" : "https://v6.ident.me";
}
