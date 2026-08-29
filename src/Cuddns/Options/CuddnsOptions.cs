using Cuddns.Providers;

namespace Cuddns.Options;

public sealed class CuddnsOptions
{
    public int IntervalSeconds { get; set; } = 300;

    public List<IProviderConfig> Providers { get; set; } = [];

    /// <summary>
    /// Validates the bound configuration, throwing a descriptive
    /// <see cref="ConfigValidationException"/> on the first problem found so startup fails fast.
    /// </summary>
    public void Validate()
    {
        if (IntervalSeconds <= 0)
        {
            throw new ConfigValidationException("intervalSeconds must be greater than 0.");
        }

        if (Providers.Count == 0)
        {
            throw new ConfigValidationException("At least one provider must be configured.");
        }

        foreach (var provider in Providers)
        {
            provider.Validate();
        }
    }
}
