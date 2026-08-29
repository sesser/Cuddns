using System.Text.RegularExpressions;

namespace Cuddns.Validation;

public static partial class Hostname
{
    public static bool IsValid(string value) => !string.IsNullOrWhiteSpace(value) && HostnameRegex().IsMatch(value);

    [GeneratedRegex(@"^(?=.{1,253}$)(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*$")]
    private static partial Regex HostnameRegex();
}
