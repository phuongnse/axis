using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Records;

/// <summary>
/// The values of a create with each left-out field set from its default, or the errors that refuse
/// the create. <see cref="Errors"/> is keyed by JSON Pointer in ordinal key order, and is null when
/// every default got a value.
/// </summary>
public sealed record RecordDefaultsResult(
    IReadOnlyList<RecordValue> Values,
    SortedDictionary<string, string[]>? Errors);

/// <summary>
/// Fills the fields a create leaves out from their compiled <c>default</c>, which is an expression
/// over no fields. A field the body sends, even as <c>null</c>, keeps its value. <c>now()</c> in a
/// default is the start time of the write transaction. A run-time error is an error at
/// <c>/values/&lt;field&gt;</c> with <see cref="RecordInputMessages.NotComputable"/>, as is a value
/// that does not fit the field. A default that gives <c>null</c> for a required field is a
/// <see cref="RecordInputMessages.Required"/> error. Only the owner's fields get defaults: the rows of
/// a child collection do not. An update never applies a default.
/// </summary>
public static class RecordDefaults
{
    public static RecordDefaultsResult Apply(EntityModel entity, IReadOnlyList<RecordValue> values, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(values);

        var columns = RecordQueries.Columns(entity);
        var given = values.Select(value => value.Field.Name).ToHashSet(StringComparer.Ordinal);
        if (!columns.Any(field => field.Default is not null && !given.Contains(field.Name)))
        {
            return new RecordDefaultsResult(values, null);
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var filled = new Dictionary<string, RecordValue>(StringComparer.Ordinal);
        foreach (var field in columns.Where(field => field.Default is not null && !given.Contains(field.Name)))
        {
            var result = ExpressionInterpreter.Evaluate(field.Default!.Syntax, field.Default.Check, new ExpressionValues([]), records: null, now: now);
            RecordValue? value = null;
            var message = result.Succeeded ? RecordComputer.ToRecordValue(field, result.Value, out value) : Messages.NotComputable;
            if (message is null && value!.Value is null && field.Required)
            {
                message = Messages.Required;
            }

            if (message is not null)
            {
                errors[$"/values/{field.Name}"] = [message];
            }
            else
            {
                filled[field.Name] = value!;
            }
        }

        if (errors.Count > 0)
        {
            return new RecordDefaultsResult(values, errors);
        }

        var byName = values.ToDictionary(value => value.Field.Name, StringComparer.Ordinal);
        return new RecordDefaultsResult(
            [.. columns.Select(field => byName.GetValueOrDefault(field.Name) ?? filled.GetValueOrDefault(field.Name)).OfType<RecordValue>()],
            null);
    }
}
