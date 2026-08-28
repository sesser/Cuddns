using Cuddns.Options;

namespace Cuddns.Providers;

/// <summary>
/// Builds an <see cref="IDnsProvider"/> bound to one configured provider block
/// (including its own credentials/region), keyed by <see cref="ProviderType"/>.
/// </summary>
public interface IDnsProviderFactory
{
    /// <summary>The <c>type</c> value in config (e.g. "route53") this factory handles.</summary>
    string ProviderType { get; }

    IDnsProvider Create(ProviderOptions provider);
}
