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

        var reported = new HashSet<(string Path, string Message)>();
        foreach (var node in results.Details ?? [])
        {
            // An "if" that fails only chooses which branch applies, so its errors are not problems.
            if (node.IsValid || node.Errors is null || IsUnderIf(node.EvaluationPath.ToString()))
            {
                continue;
            }

            var path = node.InstanceLocation.ToString();
            foreach (var (keyword, error) in node.Errors)
            {
                var message = DescribeSchemaError(node, path, keyword, error);
                if (reported.Add((path, message)))
                {
                    diagnostics.Add(new Diagnostic(code, message, file, path, resourceId));
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
        var evaluationPath = node.EvaluationPath.ToString();
        if (evaluationPath.EndsWith("/additionalProperties", StringComparison.Ordinal)
            || (string.IsNullOrEmpty(keyword) && ParentSegment(evaluationPath) == "properties"))
        {
            var property = path[(path.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            return $"Property '{property}' is not allowed here.";
        }

        return string.IsNullOrEmpty(keyword) ? error : $"{error} ({keyword})";
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
