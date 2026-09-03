namespace Cuddns.Config;

/// <summary>
/// Holds the current <see cref="ConfigSnapshot"/>, safely shared between the single writer
/// (<see cref="ConfigWatcherService"/>) and multiple readers (<see cref="Orchestration.DdnsWorker"/>).
/// Swaps are atomic and the snapshot itself is treated as immutable once built, so a plain
/// <see cref="Interlocked.Exchange{T}(ref T, T)"/>/<see cref="Volatile"/> pair is sufficient —
/// there's never a read-modify-write on the field itself.
/// </summary>
public sealed class ConfigState(ConfigSnapshot initial)
{
    private ConfigSnapshot _current = initial;

    public ConfigSnapshot Current => Volatile.Read(ref _current);

    /// <summary>
    /// Atomically swaps in <paramref name="next"/> and disposes any <see cref="IDisposable"/>
    /// providers from the snapshot it replaced (e.g. Route53's AWS SDK client) — the one
    /// place old-snapshot teardown happens, so it can't be forgotten at a call site.
    /// </summary>
    public void Replace(ConfigSnapshot next)
    {
        var previous = Interlocked.Exchange(ref _current, next);
        foreach (var provider in previous.DnsProviders)
        {
            (provider as IDisposable)?.Dispose();
        }
    }
}
