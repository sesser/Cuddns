namespace Cuddns.PublicIp;

/// <summary>
/// The current public IP per address family, as resolved by <see cref="IPublicIpResolver"/>.
/// Either may be null if no configured source could answer for that family (e.g. a host with
/// no IPv6 connectivity) — callers should treat a null family as "skip records of that type
/// this run", not as an error, unless both are null.
/// </summary>
public sealed record PublicIpResult(string? IPv4, string? IPv6);
