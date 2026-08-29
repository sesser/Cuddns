namespace Cuddns.Providers;

/// <summary>
/// Configuration owned by a single DNS provider implementation. Each provider defines its
/// own concrete type (credentials, zones, whatever shape it needs) instead of sharing a
/// generic options class, and is responsible for validating itself.
/// </summary>
public interface IProviderConfig
{
    /// <summary>The <c>type</c> value in config (e.g. "route53") that selects this provider.</summary>
    string Type { get; }

    /// <summary>Throws a <see cref="Options.ConfigValidationException"/> if this config is invalid.</summary>
    void Validate();
}
