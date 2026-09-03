using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cuddns.Config;

/// <summary>
/// Polls config.yaml (and .env, if configured) for changes and hot-reloads
/// <see cref="ConfigState"/> when one is detected. Uses plain mtime polling rather than
/// FileSystemWatcher/PhysicalFileProvider — native filesystem change events are well-known
/// to be unreliable across Docker Desktop bind mounts (macOS osxfs/gRPC-FUSE, Windows),
/// which is exactly how Cuddns's /config is normally mounted.
/// </summary>
public sealed class ConfigWatcherService(
    ConfigSnapshotBuilder snapshotBuilder,
    ConfigState state,
    string configPath,
    string? envPath,
    ILogger<ConfigWatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    // Guards against reading a file mid-write from a non-atomic save (most editors instead
    // write-then-rename, which is atomic, but this costs little and covers the rest).
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private DateTime? _lastConfigWriteUtc = SafeGetLastWriteTimeUtc(configPath);
    private DateTime? _lastEnvWriteUtc = envPath is not null ? SafeGetLastWriteTimeUtc(envPath) : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckAndReloadOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Checks both files' last-write times and, if either changed, attempts a reload.
    /// Public so tests can call it directly instead of waiting on the polling loop.
    /// Returns true if a reload happened.
    /// </summary>
    public async Task<bool> CheckAndReloadOnceAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            logger.LogDebug("Config file {ConfigPath} not found at poll time; skipping this check.", configPath);
            return false;
        }

        var configWriteUtc = File.GetLastWriteTimeUtc(configPath);
        var envWriteUtc = envPath is not null ? SafeGetLastWriteTimeUtc(envPath) : null;

        var configChanged = configWriteUtc != _lastConfigWriteUtc;
        var envChanged = envWriteUtc is not null && envWriteUtc != _lastEnvWriteUtc;

        if (!configChanged && !envChanged)
        {
            return false;
        }

        logger.LogInformation(
            "Detected change to {Path} (modified {Timestamp:o}); attempting reload.",
            configChanged ? configPath : envPath,
            configChanged ? configWriteUtc : envWriteUtc);

        await Task.Delay(SettleDelay, cancellationToken);

        // Record the mtimes now regardless of outcome, so a persistently-broken file
        // doesn't retrigger a reload attempt every poll tick — only a further edit does.
        _lastConfigWriteUtc = SafeGetLastWriteTimeUtc(configPath) ?? _lastConfigWriteUtc;
        _lastEnvWriteUtc = envPath is not null ? SafeGetLastWriteTimeUtc(envPath) : null;

        try
        {
            var newSnapshot = snapshotBuilder.Build(configPath, envPath);
            state.Replace(newSnapshot);
            logger.LogInformation("Config reloaded: {Count} provider(s) configured.", newSnapshot.DnsProviders.Count);
            return true;
        }
        catch (Exception ex)
        {
            // The thrown exception (typically ConfigValidationException) already names the
            // specific problem — a missing ${VAR}, an unknown provider type, whatever — so
            // logging it here satisfies both "log missing env var in config" and "log what
            // the detected change was" without needing exception-type-specific branches.
            logger.LogWarning(ex, "Config reload failed, keeping previous config: {Message}", ex.Message);
            return false;
        }
    }

    private static DateTime? SafeGetLastWriteTimeUtc(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
}
