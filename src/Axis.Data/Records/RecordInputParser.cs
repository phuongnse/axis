using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Records;

/// <summary>
/// Parses a record request body against an entity model, or an endpoint call's body against the
/// endpoint's input fields. Every problem is reported in one pass, keyed by RFC 6901 JSON Pointer
/// into the body; a valid body becomes typed values in the entity's field declaration order. The
/// parser has no database access.
/// </summary>
public static partial class RecordInputParser
{
    private const string ValuesProperty = "values";

    private const string InputProperty = "input";

    private const string VersionProperty = "version";

    private const string VersionPointer = "/" + VersionProperty;

    private const string DateFormat = "yyyy-MM-dd";

    // The digits of long.MaxValue; more integer digits never fit a long.
    private const int LongDigits = 19;

    // PostgreSQL's limits for numeric without a precision.
    private const long UnboundedIntegerDigits = 131072;

    private const long UnboundedFractionDigits = 16383;

    // PostgreSQL stores microseconds; the parsed fraction is in ticks of 100 ns.
    private const int TickDigits = 7;

    private static readonly TimeSpan _maxOffset = TimeSpan.FromHours(14);

    /// <summary>
    /// Parses <paramref name="utf8Body"/> against <paramref name="entity"/>. The rows of a child
    /// collection are checked against the child entity that <paramref name="application"/> declares.
    /// </summary>
    public static RecordInputResult Parse(ReadOnlyMemory<byte> utf8Body, EntityModel entity, ApplicationModel application, RecordOperation operation)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(application);

        return Parse(utf8Body, entity, application, operation, ValuesProperty);
    }

    /// <summary>
    /// Parses an endpoint call's body, <c>{ "input": { … } }</c>, against the input fields of
    /// <paramref name="endpoint"/>, by the rules of a create body with <c>input</c> in place of
    /// <c>values</c>. So errors are keyed <c>/input/&lt;name&gt;</c>, or
    /// <c>/input/&lt;collection&gt;/&lt;index&gt;/&lt;field&gt;</c> for a row of a child-collection
    /// input, whose rows are checked against its target child entity. The values are keyed by the
    /// input names, in their declaration order, and the rows name the input as their collection.
    /// </summary>
    public static RecordInputResult ParseEndpointInput(ReadOnlyMemory<byte> utf8Body, EndpointModel endpoint, ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(application);

        // The input fields act as the fields of an entity, so the body is read exactly as a create.
        var inputEntity = new EntityModel
        {
            Id = endpoint.Id,
            Name = endpoint.Name,
            File = endpoint.File,
            Fields = [.. endpoint.Input.Select(input => new FieldModel
            {
                Name = input.Name,
                Type = input.Type,
                Required = input.Required,
                MaxLength = input.MaxLength,
                Precision = input.Precision,
                Scale = input.Scale,
                Values = input.Values,
                Target = input.Target,
            })],
        };
        return Parse(utf8Body, inputEntity, application, RecordOperation.Create, InputProperty);
    }

    /// <summary>
    /// Parses the <c>values</c> of a draft, an object the caller has already found, against
    /// <paramref name="entity"/>. Unlike a create it keeps the valid values next to the errors, and
    /// it does not report a required field the draft leaves out or sets to <c>null</c> on the owner,
    /// which the caller does. The rows keep their own checks. Errors are keyed <c>/values/…</c>.
    /// Returns null when a property name cannot be read.
    /// </summary>
    public static RecordDraftResult? ParseDraft(JsonElement values, EntityModel entity, ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(application);

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
        var rows = new Dictionary<string, RecordRows>(StringComparer.Ordinal);
        var links = new Dictionary<string, RecordLinks>(StringComparer.Ordinal);
        if (!ReadValues(values, "/" + ValuesProperty, entity, application, parsed, rows, links, errors, draft: true))
        {
            return null;
        }

        var input = new RecordInput
        {
            Values = RecordQueries.Columns(entity)
                .Where(field => parsed.ContainsKey(field.Name))
                .Select(field => new RecordValue(field, parsed[field.Name]))
                .ToList(),
            Rows = entity.Fields
                .Where(field => rows.ContainsKey(field.Name))
                .Select(field => rows[field.Name])
                .ToList(),
            Links = entity.Fields
                .Where(field => links.ContainsKey(field.Name))
                .Select(field => links[field.Name])
                .ToList(),
        };
        return new RecordDraftResult(input, errors);
    }

    private static RecordInputResult Parse(
        ReadOnlyMemory<byte> utf8Body,
        EntityModel entity,
        ApplicationModel application,
        RecordOperation operation,
        string valuesProperty)
    {
        var valuesPointer = "/" + valuesProperty;

        // Checked up front so invalid bytes are a body error wherever they sit, not a value error.
        if (!Utf8.IsValid(utf8Body.Span))
        {
            return MalformedBody();
        }

        JsonDocument document;
        try
        {
            // Duplicates are allowed here so that they are reported per property.
            document = JsonDocument.Parse(utf8Body);
        }
        catch (JsonException)
        {
            return MalformedBody();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return MalformedBody();
            }

            var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            JsonElement? values = null;
            var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
            var rows = new Dictionary<string, RecordRows>(StringComparer.Ordinal);
            var links = new Dictionary<string, RecordLinks>(StringComparer.Ordinal);
            long? version = null;

            foreach (var property in root.EnumerateObject())
            {
                if (!TryReadName(property, out var name))
                {
                    return MalformedBody();
                }

                var pointer = Pointer("", name);
                if (!names.Add(name))
                {
                    AddError(errors, pointer, Messages.DuplicateProperty);
                    continue;
                }

                if (name == valuesProperty && property.Value.ValueKind == JsonValueKind.Object)
                {
                    values = property.Value;
                    if (!ReadValues(property.Value, valuesPointer, entity, application, parsed, rows, links, errors))
                    {
                        return MalformedBody();
                    }
                }
                else if (name == valuesProperty)
                {
                    AddError(errors, pointer, Messages.MustBeObject);
                }
                else if (name == VersionProperty && operation == RecordOperation.Update)
                {
                    if (TryReadVersion(property.Value, out var number))
                    {
                        version = number;
                    }
                    else
                    {
                        AddError(errors, pointer, Messages.Version);
                    }
                }
                else
                {
                    AddError(errors, pointer, Messages.UnknownProperty);
                }
            }

            if (!names.Contains(valuesProperty))
            {
                AddError(errors, valuesPointer, Messages.Required);
            }

            if (operation == RecordOperation.Update && !names.Contains(VersionProperty))
            {
                AddError(errors, VersionPointer, Messages.Required);
            }

            // Required fields are checked only when values is an object, so a missing values is one error.
            if (operation == RecordOperation.Create && values is { } valuesElement)
            {
                AddMissingRequired(valuesElement, valuesPointer, entity, owner: true, errors);
            }

            if (errors.Count > 0)
            {
                return RecordInputResult.Failure(errors);
            }

            return RecordInputResult.Success(new RecordInput
            {
                Values = RecordQueries.Columns(entity)
                    .Where(field => parsed.ContainsKey(field.Name))
                    .Select(field => new RecordValue(field, parsed[field.Name]))
                    .ToList(),
                Rows = entity.Fields
                    .Where(field => rows.ContainsKey(field.Name))
                    .Select(field => rows[field.Name])
                    .ToList(),
                Links = entity.Fields
                    .Where(field => links.ContainsKey(field.Name))
                    .Select(field => links[field.Name])
                    .ToList(),
                Version = version,
            });
        }
    }

    /// <summary>
    /// Reads the values object at <paramref name="pointer"/>, the owner's values or one row, against
    /// the fields of <paramref name="entity"/>. Returns false when a property name cannot be read.
    /// <paramref name="rows"/> and <paramref name="links"/> are null for a row, whose entity has no
    /// child collection and no references field. In a
    /// <paramref name="draft"/> a <c>null</c> is a value even for a required field, because the
    /// user may be clearing it; the caller reports it as missing.
    /// </summary>
    private static bool ReadValues(
        JsonElement values,
        string pointer,
        EntityModel entity,
        ApplicationModel application,
        Dictionary<string, object?> parsed,
        Dictionary<string, RecordRows>? rows,
        Dictionary<string, RecordLinks>? links,
        SortedDictionary<string, string[]> errors,
        bool draft = false)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in values.EnumerateObject())
        {
            if (!TryReadName(property, out var name))
            {
                return false;
            }

            var propertyPointer = Pointer(pointer, name);
            if (!names.Add(name))
            {
                AddError(errors, propertyPointer, Messages.DuplicateProperty);
                continue;
            }

            // Field names are matched exactly; EntityModel.TryGetField ignores letter case.
            var field = entity.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (field is null
                || (field.Type == FieldType.ChildCollection && rows is null)
                || (field.Type == FieldType.References && links is null))
            {
                AddError(errors, propertyPointer, Messages.UnknownProperty);
                continue;
            }

            // The server computes the value on every write, or numbers it or sets its creator on
            // create, so a body never sets it, not even to null.
            if (field.IsComputed || field.Sequence is not null || field.Creator)
            {
                AddError(errors, propertyPointer, Messages.Computed);
                continue;
            }

            if (field.Type == FieldType.ChildCollection)
            {
                if (!ReadRows(property.Value, propertyPointer, field, application, rows!, errors))
                {
                    return false;
                }

                continue;
            }

            if (field.Type == FieldType.References)
            {
                ReadLinks(property.Value, propertyPointer, field, links!, errors, draft);
                continue;
            }

            if (draft && property.Value.ValueKind == JsonValueKind.Null)
            {
                parsed[name] = null;
            }
            else if (ReadValue(property.Value, field, out var value) is { } message)
            {
                AddError(errors, propertyPointer, message);
            }
            else
            {
                parsed[name] = value;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the rows of a child collection. Each row is read like a create body, so a required
    /// field of the child must be present. Returns false when a property name cannot be read.
    /// </summary>
    private static bool ReadRows(
        JsonElement element,
        string pointer,
        FieldModel collection,
        ApplicationModel application,
        Dictionary<string, RecordRows> rows,
        SortedDictionary<string, string[]> errors)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            AddError(errors, pointer, Messages.MustBeArray);
            return true;
        }

        var child = application.FindEntity(collection.Target!.Id)
            ?? throw new InvalidOperationException("The application has no entity for the child collection.");
        var columns = RecordQueries.Columns(child);
        var parsedRows = new List<IReadOnlyList<RecordValue>>();
        var index = 0;
        foreach (var row in element.EnumerateArray())
        {
            var rowPointer = Pointer(pointer, index.ToString(CultureInfo.InvariantCulture));
            index++;
            if (row.ValueKind != JsonValueKind.Object)
            {
                AddError(errors, rowPointer, Messages.MustBeObject);
                continue;
            }

            var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (!ReadValues(row, rowPointer, child, application, parsed, rows: null, links: null, errors))
            {
                return false;
            }

            AddMissingRequired(row, rowPointer, child, owner: false, errors);

            // Every column is written, so a field the row leaves out is SQL NULL.
            parsedRows.Add([.. columns.Select(field => new RecordValue(field, parsed.GetValueOrDefault(field.Name)))]);
        }

        rows[collection.Name] = new RecordRows(collection, child, parsedRows);
        return true;
    }

    /// <summary>
    /// Reads the ids of a references field. The array replaces the stored set, so it must hold distinct
    /// ids, at most <see cref="RecordLinks.Max"/>. A malformed or repeated id is reported at its index.
    /// Too many ids are reported once at the field, without a check of each id. An empty set for a
    /// required field is <see cref="Messages.Required"/>, except in a <paramref name="draft"/>, whose
    /// caller reports it.
    /// </summary>
    private static void ReadLinks(
        JsonElement element,
        string pointer,
        FieldModel field,
        Dictionary<string, RecordLinks> links,
        SortedDictionary<string, string[]> errors,
        bool draft)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            AddError(errors, pointer, Messages.MustBeIdArray);
            return;
        }

        if (element.GetArrayLength() > RecordLinks.Max)
        {
            AddError(errors, pointer, Messages.MaxIds(RecordLinks.Max));
            return;
        }

        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        var failed = false;
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPointer = Pointer(pointer, index.ToString(CultureInfo.InvariantCulture));
            index++;
            if (ReadReference(item, out var value) is { } message)
            {
                AddError(errors, itemPointer, message);
                failed = true;
            }
            else if (!seen.Add((Guid)value!))
            {
                AddError(errors, itemPointer, Messages.DuplicateId);
                failed = true;
            }
            else
            {
                ids.Add((Guid)value);
            }
        }

        if (failed)
        {
            return;
        }

        if (ids.Count == 0 && field.Required && !draft)
        {
            AddError(errors, pointer, Messages.Required);
            return;
        }

        links[field.Name] = new RecordLinks(field, ids);
    }

    /// <summary>
    /// Reports each required column field of <paramref name="entity"/> that <paramref name="values"/> leaves out.
    /// The owner's required field with a default may be left out, because a create fills it. A row has
    /// no defaults, so it keeps every required field required.
    /// </summary>
    private static void AddMissingRequired(JsonElement values, string pointer, EntityModel entity, bool owner, SortedDictionary<string, string[]> errors)
    {
        foreach (var field in RecordQueries.Columns(entity).Where(field =>
            field.Required && !(owner && field.Default is not null) && !values.TryGetProperty(field.Name, out _)))
        {
            AddError(errors, Pointer(pointer, field.Name), Messages.Required);
        }

        // A set has no column, but a required one needs at least one id, so a create cannot leave it out.
        foreach (var field in entity.Fields.Where(field =>
            owner && field.Type == FieldType.References && field.Required && !values.TryGetProperty(field.Name, out _)))
        {
            AddError(errors, Pointer(pointer, field.Name), Messages.Required);
        }
    }

    /// <summary>Reads one field value; returns the error message, or null when the value is valid.</summary>
    private static string? ReadValue(JsonElement element, FieldModel field, out object? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return field.Required ? Messages.Required : null;
        }

        return field.Type switch
        {
            FieldType.Text => ReadText(element, field, out value),
            FieldType.Integer => ReadInteger(element, field, out value),
            FieldType.Decimal or FieldType.Money => ReadDecimal(element, field, out value),
            FieldType.Boolean => ReadBoolean(element, out value),
            FieldType.Date => ReadDate(element, field, out value),
            FieldType.DateTime => ReadDateTime(element, field, out value),
            FieldType.Enum => ReadEnum(element, field, out value),
            FieldType.Reference or FieldType.File => ReadReference(element, out value),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unknown field type."),
        };
    }

    private static string? ReadText(JsonElement element, FieldModel field, out object? value)
    {
        value = null;
        if (!TryReadString(element, out var text))
        {
            return Messages.Text;
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            return Messages.NullCharacter;
        }

        // A character outside the BMP is one code point, as PostgreSQL counts it.
        if (field.MaxLength is { } maxLength && text.EnumerateRunes().Count() > maxLength)
        {
            return Messages.MaxLength(maxLength);
        }

        if (field.MinLength is { } minLength && text.EnumerateRunes().Count() < minLength)
        {
            return Messages.MinLength(minLength);
        }

        value = text;
        return null;
    }

    private static string? ReadInteger(JsonElement element, FieldModel field, out object? value)
    {
        value = null;
        if (!TryReadLong(element, out var number))
        {
            return Messages.Integer;
        }

        if (field.Min is { } min && number < long.Parse(min, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))
        {
            return Messages.AtLeast(min);
        }

        if (field.Max is { } max && number > long.Parse(max, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))
        {
            return Messages.AtMost(max);
        }

        value = number;
        return null;
    }

    private static string? ReadDecimal(JsonElement element, FieldModel field, out object? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return Messages.Decimal;
        }

        // Values are never rounded, and the limits are checked before any text is built.
        var number = JsonNumberText.Parse(element.GetRawText());
        if (CheckDigits(field, number) is { } message)
        {
            return message;
        }

        // The limits are compared exactly, as written: a bound is never rounded to a decimal.
        if (field.Min is { } min && JsonNumberText.Compare(number, JsonNumberText.Parse(min)) < 0)
        {
            return Messages.AtLeast(min);
        }

        if (field.Max is { } max && JsonNumberText.Compare(number, JsonNumberText.Parse(max)) > 0)
        {
            return Messages.AtMost(max);
        }

        value = JsonNumberText.Render(number);
        return null;
    }

    /// <summary>
    /// Checks that a decimal number fits the field's precision and scale. Returns the error
    /// message, or null when it fits. <paramref name="rawNumber"/> is a JSON number.
    /// </summary>
    internal static string? CheckDecimalText(FieldModel field, string rawNumber) =>
        CheckDigits(field, JsonNumberText.Parse(rawNumber));

    private static string? CheckDigits(FieldModel field, JsonNumber number)
    {
        var (maxIntegerDigits, maxFractionDigits) = field.Precision is { } precision
            ? (precision - (field.Scale ?? 0), field.Scale ?? 0)
            : (UnboundedIntegerDigits, UnboundedFractionDigits);

        if (number.IntegerDigits > maxIntegerDigits)
        {
            return Messages.IntegerDigits(maxIntegerDigits);
        }

        if (number.FractionDigits > maxFractionDigits)
        {
            return Messages.FractionDigits(maxFractionDigits);
        }

        return null;
    }

    private static string? ReadBoolean(JsonElement element, out object? value)
    {
        value = null;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Messages.Boolean;
        }

        value = element.GetBoolean();
        return null;
    }

    private static string? ReadDate(JsonElement element, FieldModel field, out object? value)
    {
        value = null;
        if (!TryReadString(element, out var text)
            || !DatePattern().IsMatch(text)
            || !DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Messages.Date;
        }

        if (field.Min is { } min && date < ParseDate(min))
        {
            return Messages.AtLeast(min);
        }

        if (field.Max is { } max && date > ParseDate(max))
        {
            return Messages.AtMost(max);
        }

        value = date;
        return null;
    }

    private static string? ReadDateTime(JsonElement element, FieldModel field, out object? value)
    {
        value = null;
        if (!TryReadString(element, out var text) || DateTimePattern().Match(text) is not { Success: true } match)
        {
            return Messages.DateTime;
        }

        if (!DateOnly.TryParseExact(match.Groups["date"].ValueSpan, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Messages.DateTime;
        }

        // RFC 3339 allows a leap second, but PostgreSQL does not store one.
        var hour = ReadDigits(match.Groups["hour"]);
        var minute = ReadDigits(match.Groups["minute"]);
        var second = ReadDigits(match.Groups["second"]);
        if (hour > 23 || minute > 59 || second > 59)
        {
            return Messages.DateTime;
        }

        var offset = TimeSpan.Zero;
        if (match.Groups["sign"].Success)
        {
            var offsetMinutes = ReadDigits(match.Groups["offsetMinute"]);
            offset = new TimeSpan(ReadDigits(match.Groups["offsetHour"]), offsetMinutes, 0);
            if (offsetMinutes > 59 || offset > _maxOffset)
            {
                return Messages.DateTime;
            }

            if (match.Groups["sign"].ValueSpan[0] == '-')
            {
                offset = -offset;
            }
        }

        long fractionTicks = 0;
        if (match.Groups["fraction"] is { Success: true } fraction)
        {
            fractionTicks = ReadDigits(fraction);
            for (var digits = fraction.Length; digits < TickDigits; digits++)
            {
                fractionTicks *= 10;
            }
        }

        var localTicks = date.ToDateTime(new TimeOnly(hour, minute, second)).Ticks + fractionTicks;
        try
        {
            // Npgsql writes only offset zero to timestamp with time zone.
            value = new DateTimeOffset(localTicks, offset).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // The instant falls outside 0001..9999 in UTC.
            return Messages.DateTime;
        }

        // Instants are compared in UTC, so the offset a bound or a value is written with does not matter.
        var instant = (DateTimeOffset)value;
        if (field.Min is { } min && instant < ParseDateTime(min))
        {
            value = null;
            return Messages.AtLeast(min);
        }

        if (field.Max is { } max && instant > ParseDateTime(max))
        {
            value = null;
            return Messages.AtMost(max);
        }

        return null;
    }

    // A bound is a literal that the compiler has already checked.
    private static DateOnly ParseDate(string bound) =>
        DateOnly.ParseExact(bound, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static DateTimeOffset ParseDateTime(string bound) =>
        DateLiterals.TryParseDateTime(bound, out var instant) ? instant : throw new InvalidOperationException("The limit is not a date-time.");

    private static string? ReadEnum(JsonElement element, FieldModel field, out object? value)
    {
        value = null;
        if (!TryReadString(element, out var text) || field.Values?.Contains(text, StringComparer.Ordinal) != true)
        {
            return Messages.Enum;
        }

        value = text;
        return null;
    }

    private static string? ReadReference(JsonElement element, out object? value)
    {
        value = null;
        if (!TryReadString(element, out var text) || text.Length != 36 || !Guid.TryParseExact(text, "D", out var id))
        {
            return Messages.Reference;
        }

        value = id;
        return null;
    }

    /// <summary>Reads the version: an integral number from 1 to <see cref="long.MaxValue"/>.</summary>
    private static bool TryReadVersion(JsonElement element, out long version) =>
        TryReadLong(element, out version) && version >= 1;

    /// <summary>Reads an integral number in the 64-bit range; <c>5.0</c> and <c>5e0</c> are integral.</summary>
    private static bool TryReadLong(JsonElement element, out long number)
    {
        number = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        // The digit counts are checked before any text is built, so a huge exponent builds none.
        var parsed = JsonNumberText.Parse(element.GetRawText());
        return parsed.FractionDigits == 0
            && parsed.IntegerDigits <= LongDigits
            && long.TryParse(JsonNumberText.RenderInteger(parsed), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Reads a string value; false for another JSON type or an unpaired surrogate escape.</summary>
    private static bool TryReadString(JsonElement element, out string text)
    {
        text = "";
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            text = element.GetString()!;
            return true;
        }
        catch (InvalidOperationException)
        {
            // System.Text.Json throws this, not JsonException, for an unpaired surrogate escape.
            return false;
        }
    }

    /// <summary>Reads a property name; false for a name with an unpaired surrogate escape.</summary>
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

    private static int ReadDigits(Group group) => int.Parse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>Appends a property name to a pointer, escaping <c>~</c> and <c>/</c> as RFC 6901 requires.</summary>
    private static string Pointer(string parent, string name) =>
        $"{parent}/{name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    /// <summary>Keeps the first problem found for a pointer.</summary>
    private static void AddError(SortedDictionary<string, string[]> errors, string pointer, string message) =>
        errors.TryAdd(pointer, [message]);

    private static RecordInputResult MalformedBody() =>
        RecordInputResult.Failure(new SortedDictionary<string, string[]>(StringComparer.Ordinal) { [""] = [Messages.MalformedBody] });

    // [0-9] rather than \d, which also matches non-ASCII digits; \z rather than $, which allows a final newline.
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(
        @"^(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})[Tt](?<hour>[0-9]{2}):(?<minute>[0-9]{2}):(?<second>[0-9]{2})(?:\.(?<fraction>[0-9]{1,6}))?(?:[Zz]|(?<sign>[+-])(?<offsetHour>[0-9]{2}):(?<offsetMinute>[0-9]{2}))\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex DateTimePattern();
}
