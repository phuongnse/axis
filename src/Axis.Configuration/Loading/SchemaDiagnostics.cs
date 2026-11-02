using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Json.Schema;

namespace Axis.Configuration.Loading;

/// <summary>Validates a JSON document against a schema and reports each error as a diagnostic at its JSON Pointer.</summary>
internal static class SchemaDiagnostics
{
    private static readonly EvaluationOptions _schemaOptions = new()
    {
        OutputFormat = OutputFormat.List,
        IncludeApplicatorErrors = false,
    };

    /// <summary>
    /// Validates <paramref name="root"/> against <paramref name="schema"/>. Each error becomes a
    /// diagnostic with <paramref name="code"/> in <paramref name="file"/>. Returns whether it is valid.
    /// </summary>
    public static bool Validate(
        JsonSchema schema, JsonElement root, string code, string file, Guid? resourceId, List<Diagnostic> diagnostics)
    {
        var results = schema.Evaluate(root, _schemaOptions);
        if (results.IsValid)
        {
            return true;
        }

        var countBefore = diagnostics.Count;
        var reported = new HashSet<(string Path, string Message)>();
        var details = results.Details ?? [];
        var validBranches = details
            .Where(node => node.IsValid)
            .Select(node => (Location: node.InstanceLocation.ToString(), Path: node.EvaluationPath.ToString()))
            .ToList();
        foreach (var node in details)
        {
            // An "if" that fails only chooses which branch applies, so its errors are not problems.
            if (node.IsValid || node.Errors is null || IsUnderIf(node.EvaluationPath.ToString()))
            {
                continue;
            }

            var path = node.InstanceLocation.ToString();
            foreach (var (keyword, error) in node.Errors)
            {
                // A value of the wrong type for one alternative of a "oneOf" or "anyOf" is no problem while
                // another alternative accepts it.
                if (keyword == "type" && IsRejectedByOtherwiseValidAlternative(node, validBranches))
                {
                    continue;
                }

                var message = DescribeSchemaError(node, path, keyword, error);
                if (reported.Add((path, message)))
                {
                    diagnostics.Add(new Diagnostic(code, message, file, path, resourceId));
                }
            }
        }

        // A failed "not" has no error of its own. A file that is invalid must still report a problem.
        if (diagnostics.Count == countBefore)
        {
            diagnostics.Add(new Diagnostic(code, "The file does not match its schema.", file, "", resourceId));
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="node"/> is an alternative of a <c>oneOf</c> or <c>anyOf</c> that its value fails,
    /// while another alternative of the same keyword accepts that value.
    /// </summary>
    private static bool IsRejectedByOtherwiseValidAlternative(EvaluationResults node, List<(string Location, string Path)> validBranches)
    {
        var segments = node.EvaluationPath.ToString().Split('/');
        var location = node.InstanceLocation.ToString();
        for (var index = 1; index < segments.Length - 1; index++)
        {
            if (segments[index] is not ("oneOf" or "anyOf") || !int.TryParse(segments[index + 1], out var branch))
            {
                continue;
            }

            var prefix = string.Join('/', segments[..(index + 1)]) + "/";
            foreach (var (validLocation, validPath) in validBranches)
            {
                if (validPath.StartsWith(prefix, StringComparison.Ordinal)
                    && validPath[prefix.Length..].Split('/')[0] is var other
                    && other != branch.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    && location == validLocation)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string DescribeSchemaError(EvaluationResults node, string path, string keyword, string error)
    {
        // A property rejected by "additionalProperties": false, or by a false schema under
        // "properties", fails against the false schema, whose own message does not say which
        // property is the problem. Only a false schema fails without a keyword.
        if (IsRejectedProperty(node.EvaluationPath.ToString(), keyword))
        {
            var property = path[(path.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            return $"Property '{property}' is not allowed here.";
        }

        return string.IsNullOrEmpty(keyword) ? error : $"{error} ({keyword})";
    }

    /// <summary>Whether an error is a property that the schema does not allow, which fails against a false schema.</summary>
    private static bool IsRejectedProperty(string evaluationPath, string? keyword) =>
        evaluationPath.EndsWith("/additionalProperties", StringComparison.Ordinal)
        || (string.IsNullOrEmpty(keyword) && ParentSegment(evaluationPath) == "properties");

    /// <summary>
    /// The JSON Pointers of the properties in <paramref name="root"/> that <paramref name="schema"/> does not
    /// allow, the ones <see cref="Validate"/> reports as "Property ... is not allowed here".
    /// </summary>
    public static IReadOnlyList<string> RejectedProperties(JsonSchema schema, JsonElement root)
    {
        var results = schema.Evaluate(root, _schemaOptions);
        var rejected = new List<string>();
        if (results.IsValid)
        {
            return rejected;
        }

        foreach (var node in results.Details ?? [])
        {
            if (node.IsValid || node.Errors is null || IsUnderIf(node.EvaluationPath.ToString()))
            {
                continue;
            }

            var evaluationPath = node.EvaluationPath.ToString();
            var path = node.InstanceLocation.ToString();
            if (node.Errors.Keys.Any(keyword => IsRejectedProperty(evaluationPath, keyword)) && !rejected.Contains(path))
            {
                rejected.Add(path);
            }
        }

        return rejected;
    }

    /// <summary>Whether a schema evaluation path goes through an <c>if</c> keyword, and not a property named <c>if</c>.</summary>
    private static bool IsUnderIf(string evaluationPath)
    {
        var segments = evaluationPath.Split('/');
        for (var index = 1; index < segments.Length; index++)
        {
            if (segments[index] == "if" && segments[index - 1] != "properties")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The segment before the last one of a JSON Pointer, or an empty string when there is none.</summary>
    private static string ParentSegment(string pointer)
    {
        var segments = pointer.Split('/');
        return segments.Length >= 3 ? segments[^2] : "";
    }
}
