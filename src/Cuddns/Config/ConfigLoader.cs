using System.Text.RegularExpressions;
using Cuddns.Options;
using Cuddns.Providers;
using DotNetEnv;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cuddns.Config;

public sealed partial class ConfigLoader(IReadOnlyList<IDnsProviderFactory> catalog)
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly IReadOnlyDictionary<string, IDnsProviderFactory> _catalogByType =
        catalog.ToDictionary(f => f.ProviderType);

    /// <summary>
    /// Loads and validates the Cuddns configuration from <paramref name="configPath"/>.
    /// If <paramref name="envPath"/> exists, its values are loaded into the process
    /// environment first so they are available for ${VAR} substitution in the YAML.
    /// </summary>
    public CuddnsOptions Load(string configPath, string? envPath)
    {
        if (!File.Exists(configPath))
        {
            throw new ConfigValidationException($"Config file not found: {configPath}");
        }

        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        var rawYaml = File.ReadAllText(configPath);
        var substitutedYaml = SubstituteEnvironmentVariables(rawYaml);

        var raw = _deserializer.Deserialize<RawConfig>(substitutedYaml) ?? new RawConfig();

        var options = new CuddnsOptions
        {
            IntervalSeconds = raw.IntervalSeconds,
            EnableIpv6 = raw.EnableIpv6,
            PublicIpSources = raw.PublicIpSources,
            Providers = raw.Providers.Select(BindProvider).ToList(),
        };

        options.Validate();
        return options;
    }

    /// <summary>
    /// Resolves a provider entry's <c>type</c> against the provider catalog, then binds the
    /// rest of its fields into that provider's own <see cref="IProviderConfig"/> type. This
    /// round-trips the entry through YAML text (rather than a custom node visitor) since it's
    /// a tiny amount of work that only runs once at startup.
    /// </summary>
    private IProviderConfig BindProvider(Dictionary<string, object> rawProvider)
    {
        if (!rawProvider.TryGetValue("type", out var typeValue) ||
            typeValue is not string type ||
            string.IsNullOrWhiteSpace(type))
        {
            throw new ConfigValidationException("Each provider entry must specify a 'type'.");
        }

        if (!_catalogByType.TryGetValue(type, out var factory))
        {
            var knownTypes = string.Join(", ", _catalogByType.Keys);
            throw new ConfigValidationException(
                $"Unknown provider type '{type}'. Known provider types: {knownTypes}.");
        }

        // 'type' is used only to select the target type above — each provider's own Type
        // property is a fixed constant, not YAML-bound, so drop it before binding the rest.
        var providerFields = rawProvider.Where(kv => kv.Key != "type").ToDictionary(kv => kv.Key, kv => kv.Value);
        var providerYaml = _serializer.Serialize(providerFields);
        return (IProviderConfig)_deserializer.Deserialize(providerYaml, factory.ConfigType)!;
    }

    private static string SubstituteEnvironmentVariables(string yaml)
    {
        return EnvPlaceholderRegex().Replace(yaml, match =>
        {
            var variableName = match.Groups["name"].Value;
            var value = Environment.GetEnvironmentVariable(variableName);
            if (value is null)
            {
                throw new ConfigValidationException(
                    $"Config references ${{{variableName}}} but no environment variable with that name is set.");
            }

            return value;
        });
    }

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvPlaceholderRegex();

    private sealed class RawConfig
    {
        public int IntervalSeconds { get; set; } = 300;

        public bool EnableIpv6 { get; set; }

        public List<string>? PublicIpSources { get; set; }

        public List<Dictionary<string, object>> Providers { get; set; } = [];
    }
}
