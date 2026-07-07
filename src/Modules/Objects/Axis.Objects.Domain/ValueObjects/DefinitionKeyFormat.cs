using System.Text.RegularExpressions;

namespace Axis.Objects.Domain.ValueObjects;

internal static partial class DefinitionKeyFormat
{
    public const int MaxLength = 63;

    public static bool IsValid(string value) => KeyRegex().IsMatch(value);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,62}\z")]
    private static partial Regex KeyRegex();
}
