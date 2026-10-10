using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using Axis.Configuration.Model;
using Axis.Data.Records;

namespace Axis.Server.Processes;

/// <summary>
/// Reads the body of a task completion: <c>outcome</c>, one of the step's outcomes ignoring letter
/// case, and the optional <c>values</c> and <c>version</c>. A value is allowed only for a field the
/// task's form makes editable. The allowed values and the version are then parsed by the record
/// update parser, so they follow the record API's value format and keys. Every problem is reported
/// in one pass, keyed by JSON Pointer into the body.
/// </summary>
internal static class TaskCompletionBody
{
    private const string OutcomeProperty = "outcome";

    private const string ValuesProperty = "values";

    private const string VersionProperty = "version";

    // The record API's messages for the same problems.
    private const string MalformedBody = "Must be a JSON object.";

    private const string UnknownProperty = "Unknown property.";

    private const string DuplicateProperty = "Duplicate property.";

    private const string MustBeObject = "Must be an object.";

    private const string CannotBeSet = "Cannot be set.";

    private const string UnknownOutcome = "Must be one of the step's outcomes.";

    /// <summary>
    /// Reads <paramref name="utf8Body"/> against the task <paramref name="step"/> and its
    /// <paramref name="form"/> over <paramref name="entity"/>, the subject entity in
    /// <paramref name="release"/>.
    /// </summary>
    public static TaskCompletionResult Read(
        byte[] utf8Body,
        TaskStepModel step,
        FormModel form,
        EntityModel entity,
        ApplicationModel release)
    {
        if (!Utf8.IsValid(utf8Body))
        {
            return Malformed();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Body);
        }
        catch (JsonException)
        {
            return Malformed();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Malformed();
            }

            var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            TaskOutcomeModel? outcome = null;
            JsonElement? values = null;
            JsonElement? version = null;
            foreach (var property in root.EnumerateObject())
            {
                if (!TryReadName(property, out var name))
                {
                    return Malformed();
                }

                var pointer = Pointer("", name);
                if (!names.Add(name))
                {
                    errors.TryAdd(pointer, [DuplicateProperty]);
                    continue;
                }

                switch (name)
                {
                    case OutcomeProperty:
                        outcome = property.Value.ValueKind == JsonValueKind.String
                            ? step.Outcomes.FirstOrDefault(candidate =>
                                string.Equals(candidate.Name, property.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                            : null;
                        break;
                    case ValuesProperty when property.Value.ValueKind == JsonValueKind.Object:
                        values = property.Value;
                        break;
                    case ValuesProperty:
                        errors.TryAdd(pointer, [MustBeObject]);
                        break;
                    case VersionProperty:
                        version = property.Value;
                        break;
                    default:
                        errors.TryAdd(pointer, [UnknownProperty]);
                        break;
                }
            }

            if (outcome is null)
            {
                errors.TryAdd(Pointer("", OutcomeProperty), [UnknownOutcome]);
            }

            var allowed = new List<(string Name, string Value)>();
            if (values is { } valuesElement)
            {
                var editable = Editable(form, entity);
                foreach (var property in valuesElement.EnumerateObject())
                {
                    if (!TryReadName(property, out var name))
                    {
                        return Malformed();
                    }

                    if (editable.Contains(name))
                    {
                        allowed.Add((name, property.Value.GetRawText()));
                    }
                    else
                    {
                        errors.TryAdd(Pointer(Pointer("", ValuesProperty), name), [CannotBeSet]);
                    }
                }
            }

            // No value to write needs no version, and the record is not written at all.
            RecordInput? input = null;
            if (allowed.Count > 0)
            {
                var parsed = RecordInputParser.Parse(UpdateBody(allowed, version), entity, release, RecordOperation.Update);
                if (parsed.Errors is { } parseErrors)
                {
                    foreach (var (key, messages) in parseErrors)
                    {
                        errors.TryAdd(key, messages);
                    }
                }
                else
                {
                    input = parsed.Input;
                }
            }

            return errors.Count > 0 ? new TaskCompletionResult(null, null, errors) : new TaskCompletionResult(outcome, input, null);
        }
    }

    /// <summary>
    /// The declared names of the fields <paramref name="form"/> makes editable: listed and not
    /// read-only, and neither computed nor numbered by a sequence.
    /// </summary>
    private static HashSet<string> Editable(FormModel form, EntityModel entity) =>
        form.Sections
            .SelectMany(section => section.Fields)
            .Where(field => !field.ReadOnly)
            .Select(field => entity.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, field.Field, StringComparison.Ordinal)))
            .Where(field => field is { IsComputed: false, Sequence: null })
            .Select(field => field!.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A record update body with the allowed values as sent, and the version as sent, or none. The
    /// values keep their raw text, so the record parser reads exactly what the client sent.
    /// </summary>
    private static byte[] UpdateBody(List<(string Name, string Value)> values, JsonElement? version)
    {
        var body = new StringBuilder("{\"values\":{");
        body.AppendJoin(',', values.Select(value => $"{JsonSerializer.Serialize(value.Name)}:{value.Value}"));
        body.Append('}');
        if (version is { } sent)
        {
            body.Append(",\"version\":").Append(sent.GetRawText());
        }

        return Encoding.UTF8.GetBytes(body.Append('}').ToString());
    }

    private static bool TryReadName(JsonProperty property, out string name)
    {
        try
        {
            name = property.Name;
            return true;
        }
        catch (InvalidOperationException)
        {
            name = "";
            return false;
        }
    }

    /// <summary>Appends a property name to a pointer, escaping <c>~</c> and <c>/</c> as RFC 6901 requires.</summary>
    private static string Pointer(string parent, string name) =>
        $"{parent}/{name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    private static TaskCompletionResult Malformed() =>
        new(null, null, new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [""] = [MalformedBody] });
}

/// <summary>
/// A read completion body: the outcome and the values to write, or the errors, never both.
/// <see cref="Input"/> is null when the body writes no value.
/// </summary>
internal sealed record TaskCompletionResult(
    TaskOutcomeModel? Outcome,
    RecordInput? Input,
    SortedDictionary<string, string[]>? Errors);
