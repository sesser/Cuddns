using Cuddns.Config;
using Microsoft.Extensions.Logging;

namespace Cuddns.Orchestration;

public sealed class DdnsWorker(
    DdnsUpdateService updateService,
    ConfigState state,
    ILogger<DdnsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = state.Current.Options.IntervalSeconds;
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        try
        {
            do
            {
                var snapshot = state.Current;
                try
                {
                    await updateService.RunOnceAsync(snapshot.DnsProviders, snapshot.PublicIpResolver, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "DDNS update run failed; will retry next interval.");
                }

                // PeriodicTimer has no mutable Period — a changed intervalSeconds (picked up
                // via a config reload) requires disposing and recreating it. Takes effect
                // starting the wait that follows, not mid-wait.
                if (state.Current.Options.IntervalSeconds != intervalSeconds)
                {
                    timer.Dispose();
                    intervalSeconds = state.Current.Options.IntervalSeconds;
                    timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally
        {
            timer.Dispose();
        }
    }
}
