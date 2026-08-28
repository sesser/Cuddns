using Cuddns.Options;
using Microsoft.Extensions.Logging;

namespace Cuddns.Orchestration;

public sealed class DdnsWorker(
    DdnsUpdateService updateService,
    CuddnsOptions config,
    ILogger<DdnsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(config.IntervalSeconds));

        do
        {
            try
            {
                await updateService.RunOnceAsync(config, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DDNS update run failed; will retry next interval.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
