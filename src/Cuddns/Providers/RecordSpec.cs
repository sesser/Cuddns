using Cuddns.Options;

namespace Cuddns.Providers;

/// <summary>
/// Parses a config record entry such as <c>host.example.com</c> or <c>host.example.com:aaaa</c>
/// into a hostname and its <see cref="RecordType"/>. A bare hostname (no <c>:type</c> suffix)
/// defaults to <see cref="RecordType.A"/> so existing configs keep working unchanged.
/// </summary>
public static class RecordSpec
{
    public static (string Name, RecordType Type) Parse(string raw)
    {
        var separatorIndex = raw.IndexOf(':');
        if (separatorIndex < 0)
        {
            return (raw, RecordType.A);
        }

        var name = raw[..separatorIndex];
        var suffix = raw[(separatorIndex + 1)..];
        var type = suffix.ToLowerInvariant() switch
        {
            "a" => RecordType.A,
            "aaaa" => RecordType.AAAA,
            _ => throw new ConfigValidationException(
                $"Unknown record type ':{suffix}' in '{raw}'. Use ':a' or ':aaaa' (or omit the suffix for A)."),
        };

        return (name, type);
    }
}
