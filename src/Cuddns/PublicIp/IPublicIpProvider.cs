namespace Cuddns.PublicIp;

public interface IPublicIpProvider
{
    Task<string> GetCurrentIpAsync(CancellationToken cancellationToken);
}
