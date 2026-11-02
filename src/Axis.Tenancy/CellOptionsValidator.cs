using System.Text.RegularExpressions;

namespace Axis.Tenancy;

/// <summary>Checks the <c>Cell</c> settings of a host that is part of a cell. A message names a setting and never holds a secret.</summary>
public static partial class CellOptionsValidator
{
    /// <summary>
    /// The problems of the cell settings, or none. A host without <c>Cell:Id</c> has none, because the cell settings do not apply to it.
    /// </summary>
    public static IReadOnlyList<string> Validate(CellOptions options, TenancySource source)
    {
        ArgumentNullException.ThrowIfNull(options);

        var problems = new List<string>();
        if (!options.IsCell)
        {
            return problems;
        }

        if (source != TenancySource.Directory)
        {
            problems.Add("Cell:Id is set, so Tenancy:Source must be 'directory'.");
        }

        var hasRegion = !string.IsNullOrWhiteSpace(options.Region);
        if (!hasRegion)
        {
            problems.Add("Cell:Id is set, so Cell:Region is required.");
        }
        else if (!RegionPattern().IsMatch(options.Region!))
        {
            problems.Add($"Cell:Region '{options.Region}' must use only lowercase letters and digits.");
        }

        if (string.IsNullOrWhiteSpace(options.TenantDomain))
        {
            problems.Add("Cell:Id is set, so Cell:TenantDomain is required.");
        }

        var id = options.Id!;
        var idMatches = hasRegion
            ? id.StartsWith($"{options.Region}-", StringComparison.Ordinal) && NumberPattern().IsMatch(id[(options.Region!.Length + 1)..])
            : IdPattern().IsMatch(id);
        if (!idMatches)
        {
            var region = hasRegion ? $" and start with Cell:Region '{options.Region}'" : string.Empty;
            problems.Add($"Cell:Id '{id}' must be the region and a number, such as 'vn-1'{region}.");
        }

        return problems;
    }

    [GeneratedRegex("^[a-z0-9]+\\z")]
    private static partial Regex RegionPattern();

    [GeneratedRegex("^[1-9][0-9]*\\z")]
    private static partial Regex NumberPattern();

    [GeneratedRegex("^[a-z0-9]+-[1-9][0-9]*\\z")]
    private static partial Regex IdPattern();
}
