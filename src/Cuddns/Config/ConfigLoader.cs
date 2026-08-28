using System.Text.RegularExpressions;
using Cuddns.Options;
using DotNetEnv;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cuddns.Config;

public sealed partial class ConfigLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

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

        var options = _deserializer.Deserialize<CuddnsOptions>(substitutedYaml) ?? new CuddnsOptions();
        options.Validate();
        return options;
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
}
