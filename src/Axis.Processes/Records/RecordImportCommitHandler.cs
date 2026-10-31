using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Records;

/// <summary>
/// Runs a <c>records.importCommit</c> work item: the commit of one checked import. It reads the file in the format
/// the import stored, as the dry run does, and writes every row of the file as the uploader, with the roles stored at upload.
/// A row creates a record through <see cref="RecordCommand.CreateInTransactionAsync"/>, unless the import names a
/// <c>key</c> and the row's key cell matches a record the uploader may read, see <see cref="RecordImportKey"/>: the row
/// then updates that record through <see cref="RecordCommand.UpdateInTransactionAsync"/>, at the version the commit's
/// transaction reads, and the body leaves out the key column and the empty cells. A row passes the same checks as the record API
/// create or update: the grants, the record filters, the field lists, the defaults of a create, the
/// sequences, the creator fields, the computed fields, the validations and the <c>record.created</c> or
/// <c>record.updated</c> audit record, and it starts the data change triggers that match it. A reference cell that holds a display value is turned into a record id
/// before the row is written, as in the dry run, see <see cref="RecordImportReferences"/>. Every row is written in one transaction that holds the
/// application's start lock shared, with the application's active release, or no row is written.
/// The import ends <c>committed</c> when every row is written, with one <c>records.imported</c> audit record
/// that holds the entity and the counts of created and updated records, and <c>failed</c> when a row is refused at write time, for example on
/// a unique value that is taken or a reference to a record that does not exist. A failed commit writes nothing,
/// and its errors are those of the first row that failed, keyed <c>/rows/{n}</c> followed by the pointer into the
/// create or update body, <c>n</c> counting data rows from 1, as the errors of the dry run. The file is cleared when the
/// commit ends. An import that is gone or is no longer <c>checked</c> has nothing left to do, and neither has one
/// whose entity the active release no longer has, which fails at the empty pointer, and so has one whose file the 24-hour cleanup deleted after the commit was queued,
/// which fails at the empty pointer and writes nothing. A file with no column for the key fails at <c>/columns</c>.
/// </summary>
internal sealed partial class RecordImportCommitHandler(
    ReleaseModelCache models,
    ILoggerFactory loggerFactory,
    ILogger<RecordImportCommitHandler> logger) : IExternalWorkItemHandler
{
    private const string RowsSavepoint = "import_rows";

    public string Kind => RecordImports.CommitWorkItemKind;

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

        if (await RecordImports.LockForWorkItemAsync(connection, transaction, item.Id, cancellationToken) is not { State: RecordImportStates.Checked } import)
        {
            return await CompleteAsync(item, connection, transaction, cancellationToken);
        }

        if (import.File is null)
        {
            await RecordImports.FinishCommitAsync(
                connection,
                transaction,
                import.Id,
                RecordImportStates.Failed,
                new Dictionary<string, string[]> { [""] = [RecordImports.FileDeletedError] },
                cancellationToken);
            return await CompleteAsync(item, connection, transaction, cancellationToken);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var (created, updated) = (0, 0);
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
            var actor = new RecordActor(import.UploaderId, import.UploaderName, import.UploaderRoles);
            (created, updated) = await WriteRowsAsync(item, connection, transaction, application, release!.Value, entity, actor, import.Key, import.Format, import.File ?? [], now, errors, cancellationToken);
            if (errors.Count == 0)
            {
                await AuditRecords.AppendAsync(
                    transaction,
                    new AuditEntry(
                        import.UploaderId,
                        AuditActions.RecordsImported,
                        import.ApplicationId,
                        entity.Id,
                        RecordId: null,
                        ProcessInstanceId: null,
                        new JsonObject { ["created"] = created, ["updated"] = updated }),
                    cancellationToken);
            }
        }

        await RecordImports.FinishCommitAsync(
            connection,
            transaction,
            import.Id,
            errors.Count == 0 ? RecordImportStates.Committed : RecordImportStates.Failed,
            errors,
            cancellationToken);
        return await CompleteAsync(item, connection, transaction, cancellationToken);
    }

    // Writes every row of the file and returns how many records it created and updated. The first row that is
    // refused stops the work: its errors go to errors and every row written so far is rolled back to the savepoint,
    // which also leaves the transaction usable after a statement that failed.
    private async Task<(int Created, int Updated)> WriteRowsAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel application,
        Guid releaseId,
        EntityModel entity,
        RecordActor actor,
        string? importKey,
        string format,
        byte[] file,
        DateTimeOffset now,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        IRecordImportFile reader = format == RecordImportFormats.Xlsx ? RecordImportXlsx.Open(file) : RecordImportCsv.Open(file);
        if (reader.HeaderError is { } headerError)
        {
            errors["/columns"] = [headerError];
            return (0, 0);
        }

        if (reader.Header is not { } header)
        {
            errors["/columns"] = ["The file has no header row."];
            return (0, 0);
        }

        var (columns, headerErrors) = RecordImportColumns.Map(header, entity);
        if (columns is null)
        {
            foreach (var (pointer, messages) in headerErrors!)
            {
                errors[pointer] = messages;
            }

            return (0, 0);
        }

        var key = importKey is null ? null : RecordImportKey.Create(importKey, columns);
        if (importKey is not null && key is null)
        {
            errors["/columns"] = ["Has no column for the key."];
            return (0, 0);
        }

        var references = RecordImportReferences.Create(application, actor.Roles, columns);
        await transaction.SaveAsync(RowsSavepoint, cancellationToken);
        var (created, updated) = (0, 0);
        foreach (var row in reader.Rows())
        {
            var prefix = $"/rows/{row.Number}";
            if (row.Cells is not { } cells)
            {
                errors[prefix] = [row.Error!];
            }
            else if (await references.ResolveAsync(connection, cells, cancellationToken) is var (resolved, lookupErrors) && lookupErrors is not null)
            {
                LogReferenceRefused(item.Id, row.Number);
                foreach (var (pointer, messages) in lookupErrors)
                {
                    errors[prefix + pointer] = messages;
                }
            }
            else
            {
                var (match, keyFailure) = key is null
                    ? (null, null)
                    : await key.FindAsync(connection, transaction, application, entity, actor, resolved, loggerFactory, cancellationToken);
                var update = match is not null;
                var result = keyFailure
                    ?? (match is null
                        ? await RecordCommand.CreateInTransactionAsync(
                            connection,
                            transaction,
                            item.TenantId,
                            application,
                            releaseId,
                            entity,
                            actor,
                            columns.Body(resolved),
                            now,
                            loggerFactory,
                            cancellationToken)
                        : await RecordCommand.UpdateInTransactionAsync(
                            connection,
                            transaction,
                            item.TenantId,
                            application,
                            releaseId,
                            entity,
                            match.Id,
                            actor,
                            columns.Body(resolved, key!.Index, match.Version),
                            now,
                            loggerFactory,
                            cancellationToken));
                if (result.Outcome == RecordCommandOutcome.Written)
                {
                    if (update)
                    {
                        updated++;
                    }
                    else
                    {
                        created++;
                    }

                    continue;
                }

                LogRowRefused(item.Id, row.Number, result.Outcome);
                if (result.Errors is { } rowErrors)
                {
                    foreach (var (pointer, messages) in rowErrors)
                    {
                        errors[prefix + pointer] = messages;
                    }
                }
                else
                {
                    errors[prefix] = [RecordImportRefusals.Message(result, update)];
                }
            }

            await transaction.RollbackAsync(RowsSavepoint, cancellationToken);
            return (0, 0);
        }

        await transaction.ReleaseAsync(RowsSavepoint, cancellationToken);
        return (created, updated);
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

    [LoggerMessage(LogLevel.Warning, "Record import commit work item {ItemId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId);

    [LoggerMessage(LogLevel.Warning, "Record import {ImportId} failed its commit: the active release has no entity {EntityId}.")]
    private partial void LogNoEntity(Guid importId, Guid entityId);

    [LoggerMessage(LogLevel.Warning, "Record import commit work item {ItemId} failed at row {Row} on a display value that matches no record or more than one, so no row was written.")]
    private partial void LogReferenceRefused(Guid itemId, int row);

    [LoggerMessage(LogLevel.Warning, "Record import commit work item {ItemId} failed at row {Row} with outcome {Outcome}, so no row was written.")]
    private partial void LogRowRefused(Guid itemId, int row, RecordCommandOutcome outcome);
}
