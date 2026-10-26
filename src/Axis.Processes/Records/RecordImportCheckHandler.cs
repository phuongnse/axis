using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Records;

/// <summary>The limits of the dry run of a record import: the most data rows a file may hold.</summary>
internal sealed record RecordImportSettings(int MaxRows);

/// <summary>
/// Runs a <c>records.importCheck</c> work item: the dry run of one uploaded import file. It checks every row
/// as a create of the entity checks it, as the uploader: the row is parsed, the fields it sets must be covered
/// by the uploader's <c>create</c> grants, then the defaults, the computed fields and the entity's validations
/// run. It writes no record, no audit record and no sequence number, and it does not read storage, so it cannot
/// see a reference to a record that does not exist or a unique value that is taken. The work runs in one
/// transaction that holds the application's start lock shared, with the application's active release, the way
/// a create resolves its release.
/// The import ends <c>checked</c> when no row has an error, and <c>failed</c> with every error otherwise. A row
/// error is keyed <c>/rows/{n}</c> followed by the pointer into the create body, <c>n</c> counting data rows
/// from 1. A problem of the header is keyed <c>/columns/{n}</c> and stops the check. More rows than
/// the limit is one error at <c>/rows</c>, and the rows after the limit are not read. An import that is gone or
/// no longer pending has nothing left to do, and neither has one whose entity the active release no longer has,
/// which fails at the empty pointer.
/// </summary>
internal sealed partial class RecordImportCheckHandler(ReleaseModelCache models, RecordImportSettings settings, ILogger<RecordImportCheckHandler> logger) : IExternalWorkItemHandler
{
    public string Kind => RecordImports.CheckWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, item.Lease, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id);
            return WorkItemOutcome.LostClaim;
        }

        if (await RecordImports.LockForWorkItemAsync(connection, transaction, item.Id, cancellationToken) is not { State: RecordImportStates.Pending } import)
        {
            return await CompleteAsync(item, connection, transaction, cancellationToken);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var release = await StartLock.TakeSharedAsync(transaction, import.ApplicationId, cancellationToken);
        var application = release is { } releaseId
            ? await models.GetAsync(new WorkItemContext(item, connection, transaction), releaseId, cancellationToken)
            : null;
        if (application?.FindEntity(import.EntityId) is not { } entity)
        {
            LogNoEntity(import.Id, import.EntityId);
            errors[""] = ["The application no longer has this entity."];
        }
        else
        {
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            Check(application, entity, new RecordActor(import.UploaderId, import.UploaderName, import.UploaderRoles), import.File ?? [], now, errors);
        }

        await RecordImports.FinishCheckAsync(
            connection,
            transaction,
            import.Id,
            errors.Count == 0 ? RecordImportStates.Checked : RecordImportStates.Failed,
            errors,
            cancellationToken);
        return await CompleteAsync(item, connection, transaction, cancellationToken);
    }

    // Adds the errors of every row to errors, in the order of the file.
    private void Check(
        ApplicationModel application,
        EntityModel entity,
        RecordActor actor,
        byte[] file,
        DateTimeOffset now,
        Dictionary<string, string[]> errors)
    {
        var csv = RecordImportCsv.Open(file);
        if (csv.HeaderError is { } headerError)
        {
            errors["/columns"] = [headerError];
            return;
        }

        if (csv.Header is not { } header)
        {
            errors["/columns"] = ["The file has no header row."];
            return;
        }

        var (columns, headerErrors) = RecordImportColumns.Map(header, entity);
        if (columns is null)
        {
            foreach (var (pointer, messages) in headerErrors!)
            {
                errors[pointer] = messages;
            }

            return;
        }

        foreach (var row in csv.Rows())
        {
            if (row.Number > settings.MaxRows)
            {
                errors["/rows"] = [$"Has more than {settings.MaxRows} rows."];
                return;
            }

            var prefix = $"/rows/{row.Number}";
            if (row.Cells is not { } cells)
            {
                errors[prefix] = [row.Error!];
                continue;
            }

            var parse = RecordCommand.ParseCreate(application, entity, actor, columns.Body(cells));
            if (parse.Failure is { } failure)
            {
                Add(errors, prefix, failure.Errors!);
                continue;
            }

            if (RecordCommand.PrepareCreate(application, entity, actor, parse.Input!, form: null, now).Errors is { } prepareErrors)
            {
                Add(errors, prefix, prepareErrors);
            }
        }
    }

    private static void Add(Dictionary<string, string[]> errors, string prefix, IDictionary<string, string[]> rowErrors)
    {
        foreach (var (pointer, messages) in rowErrors)
        {
            errors[prefix + pointer] = messages;
        }
    }

    private async Task<WorkItemOutcome> CompleteAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        return WorkItemOutcome.Completed;
    }

    [LoggerMessage(LogLevel.Warning, "Record import check work item {ItemId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId);

    [LoggerMessage(LogLevel.Warning, "Record import {ImportId} failed its check: the active release has no entity {EntityId}.")]
    private partial void LogNoEntity(Guid importId, Guid entityId);
}
