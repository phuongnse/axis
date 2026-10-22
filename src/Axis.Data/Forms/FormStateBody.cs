using System.Text.Json;
using System.Text.Unicode;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Forms;

/// <summary>
/// What a form state body holds. Exactly one of <see cref="Malformed"/> and <see cref="Draft"/> is
/// set. <see cref="Malformed"/> holds the errors that make the body a <c>400</c>, keyed by RFC 6901
/// JSON Pointer. The draft's own value errors are not among them: they go in the answer.
/// </summary>
public sealed record FormStateBodyResult(
    SortedDictionary<string, string[]>? Malformed,
    Guid? RecordId,
    RecordDraftResult? Draft,
    HashSet<string>? Touched);

/// <summary>
/// Reads the body of a form state call: <c>{ "recordId": …, "values": { … }, "touched": [ … ] }</c>.
/// The body is malformed when its shape is wrong: it is not an object, <c>recordId</c> is not null
/// or a hyphenated UUID, <c>values</c> is not an object, <c>touched</c> is not an array of text, a
/// property is repeated or unknown, or one is missing. A value that does not fit its field is not a
/// shape problem, so the body stays valid and the error is in the draft.
/// </summary>
public static class FormStateBody
{
    private const string RecordIdProperty = "recordId";

    private const string ValuesProperty = "values";

    private const string TouchedProperty = "touched";

    private const int UuidLength = 36;

    /// <summary>
    /// Reads <paramref name="body"/> against <paramref name="entity"/>. <paramref name="allowRecordId"/>
    /// is false for a form whose record is not named in the body, which then rejects <c>recordId</c>.
    /// </summary>
    public static FormStateBodyResult Parse(ReadOnlyMemory<byte> body, EntityModel entity, ApplicationModel application, bool allowRecordId)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(application);

        if (!Utf8.IsValid(body.Span))
        {
            return MalformedBody();
        }

        JsonDocument document;
        try
        {
            // Duplicates are allowed here so that they are reported per property.
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return MalformedBody();
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return MalformedBody();
            }

            var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            Guid? recordId = null;
            RecordDraftResult? draft = null;
            HashSet<string>? touched = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                string name;
                try
                {
                    name = property.Name;
                }
                catch (InvalidOperationException)
                {
                    // System.Text.Json throws this, not JsonException, for an unpaired surrogate escape.
                    return MalformedBody();
                }

                var pointer = "/" + Escape(name);
                if (!names.Add(name))
                {
                    errors.TryAdd(pointer, [Messages.DuplicateProperty]);
                }
                else if (name == RecordIdProperty && allowRecordId)
                {
                    if (!TryReadRecordId(property.Value, out recordId))
                    {
                        errors.TryAdd(pointer, [Messages.Reference]);
                    }
                }
                else if (name == ValuesProperty)
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        errors.TryAdd(pointer, [Messages.MustBeObject]);
                    }
                    else if ((draft = RecordInputParser.ParseDraft(property.Value, entity, application)) is null)
                    {
                        return MalformedBody();
                    }
                }
                else if (name == TouchedProperty)
                {
                    touched = ReadTouched(property.Value, errors);
                }
                else
                {
                    errors.TryAdd(pointer, [Messages.UnknownProperty]);
                }
            }

            if (!names.Contains(ValuesProperty))
            {
                errors.TryAdd("/" + ValuesProperty, [Messages.Required]);
            }

            if (!names.Contains(TouchedProperty))
            {
                errors.TryAdd("/" + TouchedProperty, [Messages.Required]);
            }

            return errors.Count > 0
                ? new FormStateBodyResult(errors, null, null, null)
                : new FormStateBodyResult(null, recordId, draft, touched);
        }
    }

    // Only the hyphenated form names a record, as in the record API's paths. A JSON null is a new record.
    private static bool TryReadRecordId(JsonElement element, out Guid? recordId)
    {
        recordId = null;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            var text = element.GetString()!;
            if (text.Length == UuidLength && Guid.TryParseExact(text, "D", out var parsed))
            {
                recordId = parsed;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // An unpaired surrogate escape is no UUID.
        }

        return false;
    }

    private static HashSet<string>? ReadTouched(JsonElement element, SortedDictionary<string, string[]> errors)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.TryAdd("/" + TouchedProperty, [Messages.FieldNames]);
            return null;
        }

        var touched = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var pointer = $"/{TouchedProperty}/{index++}";
            try
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    touched.Add(item.GetString()!);
                    continue;
                }
            }
            catch (InvalidOperationException)
            {
                // An unpaired surrogate escape is no text.
            }

            errors.TryAdd(pointer, [Messages.Text]);
        }

        return touched;
    }

    /// <summary>Escapes <c>~</c> and <c>/</c> in a name as RFC 6901 requires.</summary>
    private static string Escape(string name) =>
        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static FormStateBodyResult MalformedBody() =>
        new(new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [""] = [Messages.MalformedBody] }, null, null, null);
}
