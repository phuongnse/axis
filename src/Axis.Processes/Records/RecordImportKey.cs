using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Policy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Records;

/// <summary>The record a keyed import row updates: its id and the version read when the row was matched.</summary>
internal sealed record RecordImportMatch(Guid Id, long Version);

/// <summary>
/// The key of a record import: the unique field whose value in a row picks the record the row updates. A row whose key
/// cell is empty or matches no record, or no record the uploader may read, matches nothing, and the import creates a
/// record for it. A hidden record is never revealed: a key value that matches a record the uploader cannot read is no
/// match. A record filter of the uploader's <c>read</c> grants that reads a computed field that is being
/// recomputed fails the row as a pending filter.
/// </summary>
internal sealed class RecordImportKey
{
    private readonly RecordImportColumns _columns;

    private RecordImportKey(RecordImportColumns columns, int index)
    {
        _columns = columns;
        Index = index;
    }

    /// <summary>The position of the key column in a row of cells.</summary>
    public int Index { get; }

    /// <summary>
    /// The key of an import that names <paramref name="key"/>, matched to a column ignoring letter case, or
    /// <see langword="null"/> when the file has no such column.
    /// </summary>
    public static RecordImportKey? Create(string key, RecordImportColumns columns)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(columns);

        for (var index = 0; index < columns.Fields.Count; index++)
        {
            if (columns.Fields[index].Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return new RecordImportKey(columns, index);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the record of <paramref name="cells"/>' key value that <paramref name="actor"/> may read. The result holds
    /// the match, or <see langword="null"/> for none, or the failure of a pending filter.
    /// </summary>
    public async Task<(RecordImportMatch? Match, RecordCommandResult? Failure)> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel application,
        EntityModel entity,
        RecordActor actor,
        IReadOnlyList<string> cells,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (_columns.Value(application, entity, cells, Index) is not { } value
            || await RecordQueries.FindByValueAsync(connection, entity, value, cancellationToken) is not var (id, version))
        {
            return (null, null);
        }

        var read = PolicyEvaluator.Access(application, actor.Roles, entity, PolicyAction.Read);
        if (!read.Granted)
        {
            return (null, null);
        }

        if (read.Filters is { } filters)
        {
            if (await RecordCommand.ReadsPendingAsync(connection, transaction, application, entity, filters, loggerFactory, cancellationToken))
            {
                return (null, new RecordCommandResult(RecordCommandOutcome.PendingFilter));
            }

            if (await RecordCommand.MatchAsync(connection, transaction, application, entity, id, filters, actor.Id, loggerFactory, cancellationToken) is not { Found: true, Matched: true })
            {
                return (null, null);
            }
        }

        return (new RecordImportMatch(id, version), null);
    }
}
