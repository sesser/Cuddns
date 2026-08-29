namespace Cuddns.Providers;

/// <summary>
/// Builds an <see cref="IDnsProvider"/> from its own <see cref="IProviderConfig"/>. One
/// factory per provider type; the set of registered factories is the provider catalog
/// (see <c>Program.cs</c>) used both for config binding and for enumerating available
/// provider types.
/// </summary>
public interface IDnsProviderFactory
{
    /// <summary>The <c>type</c> value in config (e.g. "route53") this factory handles.</summary>
    string ProviderType { get; }

    /// <summary>The concrete <see cref="IProviderConfig"/> type this factory binds config into.</summary>
    Type ConfigType { get; }

    /// <summary>An empty/default config instance, e.g. for a future "add provider" UI flow.</summary>
    IProviderConfig CreateDefaultConfig();

    IDnsProvider Create(IProviderConfig config);
}
