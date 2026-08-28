namespace Cuddns.PublicIp;

public sealed class IfConfigNetPublicIpProvider(HttpClient httpClient) : IPublicIpProvider
{
    private const int MaxAttempts = 2;

    public async Task<string> GetCurrentIpAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var response = await httpClient.GetStringAsync("https://ifconfig.net", cancellationToken);
                var ip = response.Trim();
                if (ip.Length == 0)
                {
                    throw new InvalidOperationException("ifconfig.net returned an empty response.");
                }

                return ip;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Failed to determine current public IP from ifconfig.net.", lastError);
    }
}
