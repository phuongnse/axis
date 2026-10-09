using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Expressions.Evaluation;
using Messages = Axis.Data.Records.RecordInputMessages;

namespace Axis.Data.Records;

/// <summary>
/// The rows an owner's aggregates read, shared by <see cref="RecordComputer"/> and
/// <see cref="RecordValidator"/> so that both read the same rows. A collection the body sends is
/// read from the body's rows, with their computed fields already set. A collection an update
/// leaves out is read from the stored rows. Otherwise it has no rows.
/// </summary>
internal static class RecordCollections
{
    /// <summary>
    /// Sets each child collection of <paramref name="entity"/> in <paramref name="record"/> to its
    /// rows, as the interpreter reads them. A row value that cannot be held exactly is an error at
    /// <c>/values/&lt;collection&gt;/&lt;index&gt;/&lt;field&gt;</c>. Every collection is still read,
    /// and then the result is false, because no aggregate could be evaluated without rounding.
    /// </summary>
    public static bool TryAdd(
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordRows> rows,
        Dictionary<string, (object? Value, bool Exact)> record,
        SortedDictionary<string, string[]> errors)
    {
        var allExact = true;
        foreach (var field in entity.Fields.Where(field => field.Type == FieldType.ChildCollection))
        {
            var items = new List<ExpressionValues>();
            if (rows.FirstOrDefault(collection => collection.Collection.Name == field.Name) is { } sent)
            {
                for (var index = 0; index < sent.Rows.Count; index++)
                {
                    var row = new List<KeyValuePair<string, object?>>();
                    foreach (var value in sent.Rows[index])
                    {
                        var converted = RecordClrValues.FromInput(value, out var exact);
                        Add(row, field, index, value.Field, converted, exact);
                    }

                    items.Add(new ExpressionValues(row));
                }
            }
            else if (stored?.Values.GetValueOrDefault(field.Name) is JsonArray storedRows)
            {
                var child = application.FindEntity(field.Target!.Id)
                    ?? throw new InvalidOperationException("The application has no entity for the child collection.");
                var columns = RecordQueries.Columns(child);
                for (var index = 0; index < storedRows.Count; index++)
                {
                    var storedRow = storedRows[index] as JsonObject
                        ?? throw new InvalidOperationException("A stored row is not a JSON object.");
                    var row = new List<KeyValuePair<string, object?>>();
                    foreach (var column in columns)
                    {
                        var exact = RecordClrValues.TryFromStored(column, storedRow[column.Name], out var converted);
                        Add(row, field, index, column, converted, exact);
                    }

                    items.Add(new ExpressionValues(row));
                }
            }

            record[field.Name] = (items, true);
        }

        return allExact;

        void Add(List<KeyValuePair<string, object?>> row, FieldModel collection, int index, FieldModel column, object? value, bool exact)
        {
            if (!exact)
            {
                allExact = false;
                errors.TryAdd($"/values/{collection.Name}/{index}/{column.Name}", [Messages.NotEvaluable]);
            }

            row.Add(KeyValuePair.Create(column.Name, value));
        }
    }
}
