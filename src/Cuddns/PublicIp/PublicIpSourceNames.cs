namespace Cuddns.PublicIp;

/// <summary>
/// Canonical names for the built-in public-IP sources, used both for config's optional
/// <c>publicIpSources</c> override and to key the source catalog in Program.cs. The default
/// order (used when config doesn't specify one) is <see cref="All"/>: ifconfig.net first to
/// keep existing installs' behavior unchanged, then the family-pinned sources that can
/// actually answer for IPv6.
/// </summary>
public static class PublicIpSourceNames
{
    public const string IfConfig = "ifconfig";
    public const string Ipify = "ipify";
    public const string Icanhazip = "icanhazip";
    public const string IdentMe = "identme";

    public static readonly IReadOnlyList<string> All = [IfConfig, Ipify, Icanhazip, IdentMe];
}
