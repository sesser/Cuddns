using System.Net;

namespace Cuddns.PublicIp;

public sealed class IfConfigNetPublicIpSource(HttpClient httpClient) : IPublicIpSource
{
    private const int MaxAttempts = 2;

    public string Name => "ifconfig";

    public async Task<string?> TryGetIpAsync(IpFamily family, CancellationToken cancellationToken)
    {
        // ifconfig.net has a single endpoint with no way to pin the address family, so it can
        // only be trusted for IPv4 (whichever family the outbound connection happens to use).
        // IPv6 detection is left to family-pinned sources like ipify/icanhazip/ident.me.
        if (family == IpFamily.IPv6)
        {
            return null;
        }

        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var response = await httpClient.GetStringAsync("https://ifconfig.net", cancellationToken);
                var ip = response.Trim();

                // ifconfig.net serves an HTML page instead of plain text to clients it doesn't
                // recognize as a CLI tool; validating the result is a real IP (rather than trusting
                // it) is what stops a bad response from ever being written into a DNS record.
                if (!IPAddress.TryParse(ip, out _))
                {
                    throw new InvalidOperationException(
                        $"ifconfig.net returned an unexpected response that isn't an IP address: '{Truncate(ip)}'");
                }

                return ip;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Failed to determine current public IP from ifconfig.net.", lastError);
    }

    private static string Truncate(string value) => value.Length <= 100 ? value : value[..100] + "...";
}
