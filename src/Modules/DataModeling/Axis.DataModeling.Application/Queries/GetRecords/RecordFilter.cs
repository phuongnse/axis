using System.Text.RegularExpressions;

namespace Axis.DataModeling.Application.Queries.GetRecords;

/// <summary>
/// A single per-field filter applied to a records query.
/// Parsed from the API query param format: "{field}:{op}:{value}".
/// Operators: eq, contains, gt, lt, isEmpty, isNotEmpty.
/// </summary>
public sealed record RecordFilter(string Field, string Op, string Value)
{
    public static readonly IReadOnlySet<string> ValidOps =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "eq", "contains", "gt", "lt", "isEmpty", "isNotEmpty"
        };

    /// <summary>
    /// Parses "fieldName:op:value" → RecordFilter. Returns null if the format is invalid.
    /// For isEmpty/isNotEmpty, value segment is ignored.
    /// </summary>
    public static RecordFilter? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Split on first two colons only so the value itself can contain colons
        int first = raw.IndexOf(':');
        if (first < 0)
            return null;

        string field = raw[..first];
        string rest = raw[(first + 1)..];

        int second = rest.IndexOf(':');
        string op = second < 0 ? rest : rest[..second];
        string value = second < 0 ? string.Empty : rest[(second + 1)..];

        if (!ValidOps.Contains(op))
            return null;

        // Field name must match DataModel's FieldNameRegex: letter then alphanumeric/underscore
        if (!Regex.IsMatch(field, @"^[A-Za-z][A-Za-z0-9_]{0,63}$"))
            return null;

        return new RecordFilter(field, op.ToLowerInvariant(), value);
    }
}
