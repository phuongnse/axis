using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Axis.Configuration.Packages;

/// <summary>
/// A version range of an application's <c>packages</c> entry: an exact version <c>1.2.0</c>,
/// <c>^1.2.0</c> for any version from <c>1.2.0</c> with the same major version, or <c>~1.2.0</c>
/// for any version from <c>1.2.0</c> with the same major and minor version. Versions are plain
/// <c>major.minor.patch</c> without leading zeros. <c>^0.2.0</c> stops below <c>1.0.0</c>.
/// </summary>
public sealed partial class VersionRange
{
    private readonly char? _operator;
    private readonly string[] _parts;

    private VersionRange(char? @operator, string[] parts)
    {
        _operator = @operator;
        _parts = parts;
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out VersionRange? range)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = RangePattern().Match(text);
        range = match.Success
            ? new VersionRange(
                match.Groups["operator"].Success ? match.Groups["operator"].Value[0] : null,
                match.Groups["version"].Value.Split('.'))
            : null;
        return range is not null;
    }

    /// <summary>Whether <paramref name="version"/>, a <c>major.minor.patch</c> version, is in the range.</summary>
    public bool Matches(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!VersionPattern().IsMatch(version))
        {
            return false;
        }

        var parts = version.Split('.');
        var samePrefix = _operator switch
        {
            '^' => 1,
            '~' => 2,
            _ => 3,
        };
        for (var index = 0; index < samePrefix; index++)
        {
            if (!string.Equals(parts[index], _parts[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return PackageVersions.Compare(version, string.Join('.', _parts)) >= 0;
    }

    [GeneratedRegex(@"^(?<operator>[\^~])?(?<version>(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))$")]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")]
    private static partial Regex VersionPattern();
}
