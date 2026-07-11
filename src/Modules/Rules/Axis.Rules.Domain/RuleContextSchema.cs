using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial record RuleContextField
{
    private RuleContextField(string path, string displayName, RuleValueType type, bool allowMultiple)
    {
        Path = path;
        DisplayName = displayName;
        Type = type;
        AllowMultiple = allowMultiple;
    }

    public string Path { get; }
    public string DisplayName { get; }
    public RuleValueType Type { get; }
    public bool AllowMultiple { get; }

    public static Result<RuleContextField> Create(
        string path,
        string displayName,
        RuleValueType type,
        bool allowMultiple = false)
    {
        string normalizedPath = path?.Trim() ?? string.Empty;
        if (!ContextPathPattern().IsMatch(normalizedPath))
            return Result.Failure<RuleContextField>("Rule context path format is invalid.");

        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<RuleContextField>("Rule context field display name is required.");

        if (!Enum.IsDefined(type))
            return Result.Failure<RuleContextField>("Rule context field type is not supported.");

        return new RuleContextField(normalizedPath, displayName.Trim(), type, allowMultiple);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ContextPathPattern();
}

public sealed record RuleContextSchema
{
    private RuleContextSchema(
        RuleContextKey key,
        int version,
        RuleScope scope,
        string displayName,
        IReadOnlyList<RuleContextField> fields,
        string? targetTypeKey,
        IReadOnlyDictionary<string, IReadOnlyList<string>> configuration)
    {
        Key = key;
        Version = version;
        Scope = scope;
        DisplayName = displayName;
        Fields = fields;
        TargetTypeKey = targetTypeKey;
        Configuration = configuration;
    }

    public RuleContextKey Key { get; }
    public int Version { get; }
    public RuleScope Scope { get; }
    public string DisplayName { get; }
    public IReadOnlyList<RuleContextField> Fields { get; }
    public string? TargetTypeKey { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Configuration { get; }

    public static Result<RuleContextSchema> Create(
        string key,
        int version,
        RuleScope scope,
        string displayName,
        IReadOnlyList<RuleContextField> fields,
        string? targetTypeKey = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? configuration = null)
    {
        Result<RuleContextKey> contextKey = RuleContextKey.Create(key);
        if (contextKey.IsFailure)
            return Result.Failure<RuleContextSchema>(contextKey.Error);

        if (version <= 0)
            return Result.Failure<RuleContextSchema>("Rule context schema version must be positive.");

        if (!Enum.IsDefined(scope))
            return Result.Failure<RuleContextSchema>("Rule context scope is not supported.");

        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<RuleContextSchema>("Rule context display name is required.");

        if (fields is null || fields.Count == 0)
            return Result.Failure<RuleContextSchema>("Rule context schema must define at least one field.");

        if (fields.Any(field => field is null))
            return Result.Failure<RuleContextSchema>("Rule context schema fields are invalid.");

        if (fields.Select(field => field.Path).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            return Result.Failure<RuleContextSchema>("Rule context field paths must be unique.");

        string? normalizedTargetType = string.IsNullOrWhiteSpace(targetTypeKey)
            ? null
            : targetTypeKey.Trim();
        Dictionary<string, IReadOnlyList<string>> normalizedConfiguration = new(StringComparer.Ordinal);
        foreach ((string rawKey, IReadOnlyList<string> rawValues) in configuration
                     ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal))
        {
            if (rawKey is null || rawValues is null || rawValues.Any(value => value is null))
                return Result.Failure<RuleContextSchema>("Rule context configuration is invalid.");

            string normalizedKey = rawKey.Trim();
            string[] normalizedValues = rawValues
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (normalizedKey.Length == 0 || normalizedValues.Length == 0)
                return Result.Failure<RuleContextSchema>("Rule context configuration is invalid.");

            if (!normalizedConfiguration.TryAdd(normalizedKey, Array.AsReadOnly(normalizedValues)))
                return Result.Failure<RuleContextSchema>("Rule context configuration keys must be unique.");
        }

        return new RuleContextSchema(
            contextKey.Value,
            version,
            scope,
            displayName.Trim(),
            Array.AsReadOnly(fields.OrderBy(field => field.Path, StringComparer.Ordinal).ToArray()),
            normalizedTargetType,
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(normalizedConfiguration));
    }

    public RuleContextField? FindField(string path) =>
        Fields.FirstOrDefault(field => field.Path.Equals(path, StringComparison.Ordinal));
}
