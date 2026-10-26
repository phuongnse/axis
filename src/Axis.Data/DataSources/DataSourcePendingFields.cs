using Axis.Configuration.Model;
using Axis.Data.Records;
using Npgsql;

namespace Axis.Data.DataSources;

/// <summary>
/// Finds the fields a data source query would decide from while they are being recomputed. A sort, a
/// group field, a measure, a measure filter or a filter that reads a pending computed field would order, group, sum or
/// select by a stale stored value. A projected field is not checked, because it shows its stored value.
/// </summary>
public static class DataSourcePendingFields
{
    /// <summary>
    /// Returns an error message for each part of the query that reads a pending field, keyed
    /// <c>sort</c>, <c>filter</c> or <c>aggregate</c>, or an empty dictionary when none does. The
    /// <paramref name="sort"/> is the order the request uses. A path through reference fields reads a
    /// field of each entity on it, and a filter or a measure filter includes the body of every rule it calls.
    /// A measure filter is keyed <c>aggregate</c>. The messages
    /// name the field, never a value.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string[]>> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel application,
        DataSourceModel dataSource,
        DataSourceSort? sort,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(dataSource);

        var sorted = new List<(Guid EntityId, string Field, string Name)>();
        var aggregated = new List<(Guid EntityId, string Field, string Name)>();
        var filtered = DataSourceQueries.FilterFieldsRead(application, dataSource);

        if (sort is not null)
        {
            var read = sort.Field ?? sort.Measure?.Field;
            if (read is not null)
            {
                AddPath(dataSource, read, sorted, sort.Field is not null ? read.Name : sort.Measure!.Name);
            }
        }

        if (dataSource.Aggregate is { } aggregate)
        {
            foreach (var field in aggregate.GroupBy)
            {
                AddPath(dataSource, field, aggregated, field.Name);
            }

            foreach (var measure in aggregate.Measures)
            {
                if (measure.Field is { } field)
                {
                    AddPath(dataSource, field, aggregated, measure.Name);
                }
            }

            // Like the data source filter, a measure filter names the path it reads. It decides which rows a
            // measure counts, so it is reported under aggregate, even for a count.
            aggregated.AddRange(DataSourceQueries.MeasureFilterFieldsRead(application, dataSource)
                .Select(read => (read.EntityId, read.Field, Name: read.Path)));
        }

        var entityIds = sorted.Concat(aggregated).Select(read => read.EntityId)
            .Concat(filtered.Select(read => read.EntityId))
            .Distinct()
            .ToList();
        var pending = await PendingComputedFields.ReadAsync(connection, transaction, entityIds, cancellationToken);
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (pending.Count == 0)
        {
            return errors;
        }

        Collect(errors, "sort", sorted, pending);
        Collect(errors, "filter", filtered.Select(read => (read.EntityId, read.Field, Name: read.Path)), pending);
        Collect(errors, "aggregate", aggregated, pending);
        return errors;
    }

    // Path[0] is a field of the root entity, and each later field belongs to the target of the one before.
    private static void AddPath(
        DataSourceModel dataSource, DataSourceFieldModel field, List<(Guid EntityId, string Field, string Name)> reads, string name)
    {
        var entityId = dataSource.Entity.Id;
        foreach (var step in field.Path)
        {
            reads.Add((entityId, step.Name, name));
            entityId = step.Target?.Id ?? Guid.Empty;
        }
    }

    private static void Collect(
        SortedDictionary<string, string[]> errors,
        string key,
        IEnumerable<(Guid EntityId, string Field, string Name)> reads,
        IReadOnlyDictionary<Guid, IReadOnlySet<string>> pending)
    {
        var messages = reads
            .Where(read => pending.TryGetValue(read.EntityId, out var names) && names.Contains(read.Field))
            .Select(read => $"The field '{read.Name}' is being recomputed.")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (messages.Length > 0)
        {
            errors[key] = messages;
        }
    }
}
