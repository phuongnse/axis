using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Records;

/// <summary>
/// The values and rows of the record an endpoint call creates, or the errors that refuse the call.
/// <see cref="Errors"/> is keyed by JSON Pointer in ordinal key order, and is null when every
/// mapped field got a value.
/// </summary>
public sealed record EndpointMapResult(
    IReadOnlyList<RecordValue> Values,
    IReadOnlyList<RecordRows> Rows,
    SortedDictionary<string, string[]>? Errors);

/// <summary>
/// Maps a call's parsed input to the fields of the endpoint's entity. Each mapped field that is not
/// a child collection gets the value of its expression over the input fields, where an input the
/// body leaves out is <c>null</c>. A run-time error is an error at <c>/values/&lt;field&gt;</c>, as
/// is a value that does not fit the field. An input that cannot be held exactly is an error at
/// <c>/input/&lt;name&gt;</c>, and then no expression is evaluated. A child collection takes the
/// rows of its input, which were already parsed against its child entity. Fields the endpoint does
/// not map are left out, so they get their default, or <c>null</c> without one, as in a record API
/// create. A required field that ends up <c>null</c> is an error at <c>/values/&lt;field&gt;</c>, as it
/// is in a create body, except that a required field with a default may be left out.
/// </summary>
public static class EndpointInputMapper
{
    public static EndpointMapResult Map(ApplicationModel application, EntityModel entity, EndpointModel endpoint, RecordInput input)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(input);

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var parsed = input.Values.ToDictionary(value => value.Field.Name, StringComparer.Ordinal);
        var inputs = new List<KeyValuePair<string, object?>>();
        foreach (var field in endpoint.Input.Where(field => field.Type != FieldType.ChildCollection))
        {
            object? value = null;
            if (parsed.TryGetValue(field.Name, out var given))
            {
                value = RecordClrValues.FromInput(given, out var exact);
                if (!exact)
                {
                    errors.TryAdd($"/input/{field.Name}", [Messages.NotEvaluable]);
                }
            }

            inputs.Add(KeyValuePair.Create(field.Name, value));
        }

        if (errors.Count > 0)
        {
            return new EndpointMapResult([], [], errors);
        }

        var expressionValues = new ExpressionValues(inputs);
        var values = new Dictionary<string, RecordValue>(StringComparer.Ordinal);
        var rows = new Dictionary<string, RecordRows>(StringComparer.Ordinal);
        foreach (var mapping in endpoint.Values)
        {
            if (!entity.TryGetField(mapping.Field, out var field))
            {
                throw new InvalidOperationException($"The mapped field '{mapping.Field}' of endpoint '{endpoint.Name}' is not a field of '{entity.Name}'.");
            }

            if (mapping.CollectionInput is { } collectionInput)
            {
                // A collection the body leaves out has no rows, as in a record API create.
                if (input.Rows.FirstOrDefault(candidate => candidate.Collection.Name == collectionInput) is { } given)
                {
                    rows[field.Name] = given with { Collection = field };
                }

                continue;
            }

            var result = ExpressionInterpreter.Evaluate(mapping.Expression!.Syntax, mapping.Expression.Check, expressionValues, timeZone: application.TimeZone);
            RecordValue? value = null;
            var message = result.Succeeded ? RecordComputer.ToRecordValue(field, result.Value, out value) : Messages.NotComputable;
            if (message is not null)
            {
                errors.TryAdd($"/values/{field.Name}", [message]);
            }
            else
            {
                values[field.Name] = value!;
            }
        }

        // A record API create refuses a required field it leaves out or sets to null, and so does a call.
        // A required field with a default may be left out, because the create fills it from its default.
        foreach (var field in RecordQueries.Columns(entity).Where(field => field.Required && !field.IsComputed && field.Sequence is null && !field.Creator))
        {
            if (field.Default is not null && !values.ContainsKey(field.Name))
            {
                continue;
            }

            if (values.GetValueOrDefault(field.Name)?.Value is null)
            {
                errors.TryAdd($"/values/{field.Name}", [Messages.Required]);
            }
        }

        return errors.Count > 0
            ? new EndpointMapResult([], [], errors)
            : new EndpointMapResult(
                [.. RecordQueries.Columns(entity).Where(field => values.ContainsKey(field.Name)).Select(field => values[field.Name])],
                [.. entity.Fields.Where(field => rows.ContainsKey(field.Name)).Select(field => rows[field.Name])],
                null);
    }
}
