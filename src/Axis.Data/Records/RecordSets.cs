using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Expressions.Evaluation;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Records;

/// <summary>
/// The target records of each <c>references</c> field of an owner, as an aggregate such as
/// <c>count(software)</c> or <c>any(software, name == 'Office')</c> reads them. The rows of a set
/// keep the order of its ids. An id that names no record has no row. The read ignores the acting
/// user's read grants and record filters on the target entity, because a validation or a process
/// condition is a rule of the data. A target's computed fields are read as stored.
/// </summary>
public sealed class RecordSets
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ExpressionValues>> _rows;

    public RecordSets(
        IReadOnlyDictionary<string, IReadOnlyList<ExpressionValues>> rows,
        (string SetField, string Entity, string Field)? inexact = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _rows = rows;
        Inexact = inexact;
    }

    /// <summary>No set has a row. Read a stored set with <see cref="ReadAsync"/> instead.</summary>
    public static RecordSets Empty { get; } = new(new Dictionary<string, IReadOnlyList<ExpressionValues>>());

    /// <summary>
    /// The set field, the target entity and the target field of the first stored decimal that
    /// cannot be held exactly, or <see langword="null"/> when every value could be read.
    /// </summary>
    public (string SetField, string Entity, string Field)? Inexact { get; }

    /// <summary>The rows of the <c>references</c> field <paramref name="field"/>, or none when it has no entry.</summary>
    public IReadOnlyList<ExpressionValues> RowsOf(string field) =>
        _rows.TryGetValue(field, out var rows) ? rows : [];

    /// <summary>
    /// Reads the target records of every <c>references</c> field of <paramref name="entity"/>, with
    /// one command per field that has ids, on <paramref name="connection"/>. The ids of a field come
    /// from <paramref name="links"/> when the body sends the field, otherwise from the
    /// <paramref name="stored"/> record, otherwise the set is empty. An entity without a
    /// <c>references</c> field runs no command. Reading stops at the first stored decimal that cannot
    /// be held exactly, and the result then has <see cref="Inexact"/> set.
    /// </summary>
    public static async Task<RecordSets> ReadAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel entity,
        Record? stored,
        IReadOnlyList<RecordLinks> links,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(links);

        var rows = new Dictionary<string, IReadOnlyList<ExpressionValues>>(StringComparer.Ordinal);
        foreach (var field in entity.Fields.Where(field => field.Type == FieldType.References))
        {
            var ids = links.FirstOrDefault(link => link.Field.Name == field.Name)?.Ids
                ?? (stored?.Values.GetValueOrDefault(field.Name) is JsonArray storedIds
                    ? [.. storedIds.Select(id => Guid.Parse(id!.GetValue<string>()))]
                    : []);
            if (ids.Count == 0)
            {
                continue;
            }

            var target = application.FindEntity(field.Target!.Id)
                ?? throw new InvalidOperationException("The application has no entity for the references field.");
            var columns = RecordQueries.Columns(target);
            var read = new Dictionary<Guid, ExpressionValues>();
            var row = EntityNaming.Quote(RecordQueries.RowAlias);
            await using (var command = new NpgsqlCommand(
                $"SELECT {RecordQueries.SelectList(target, RecordQueries.RowAlias)} FROM {RecordQueries.Table(target)} AS {row}{RecordQueries.LabelJoins(target, RecordQueries.RowAlias)} WHERE {row}.{EntityNaming.Quote(EntityNaming.IdColumn)} = ANY(@ids)",
                connection))
            {
                command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() });
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var record = RecordQueries.ReadRecord(reader, target);
                    var values = new List<KeyValuePair<string, object?>>(columns.Count);
                    foreach (var column in columns)
                    {
                        if (!RecordClrValues.TryFromStored(column, record.Values.GetValueOrDefault(column.Name), out var value))
                        {
                            return new RecordSets(rows, (field.Name, target.Name, column.Name));
                        }

                        values.Add(KeyValuePair.Create(column.Name, value));
                    }

                    read[record.Id] = new ExpressionValues(values);
                }
            }

            rows[field.Name] = [.. ids.Where(read.ContainsKey).Select(id => read[id])];
        }

        return new RecordSets(rows);
    }
}
