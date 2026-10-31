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
/// Runs a <c>records.importCheck</c> work item: the dry run of one uploaded import file, a CSV file or an <c>xlsx</c>
/// workbook, whichever format the import stored. It checks every row
/// as a create of the entity checks it, as the uploader: the row is parsed, the fields it sets must be covered
/// by the uploader's <c>create</c> grants, then the defaults, the computed fields and the entity's validations
/// run. A reference cell that holds a display value instead of a record id is looked up in storage first, see
/// <see cref="RecordImportReferences"/>, and a value that matches no record or more than one is an error at its
/// field. It writes no record, no audit record and no sequence number, and it reads no other storage, so it cannot
/// see a reference to a record id that does not exist or a unique value that is taken. The work runs in one
/// transaction that holds the application's start lock shared, with the application's active release, the way
/// a create resolves its release.
/// An import that names a <c>key</c> also reads the record that each row's key cell matches, see <see cref="RecordImportKey"/>.
/// A row that matches a record the uploader may read is checked as an update of it instead, as the record command
/// checks an update: the uploader's <c>update</c> grant, its record filters and its field lists, then the parsing,
/// the computed fields and the validations, with the key column and the empty cells left out of the body. It
/// writes nothing for it either, and a row refused as an update is keyed <c>/rows/{n}</c>, or followed by the pointer
/// into the update body. A file with no column for the key is one error at <c>/columns</c>.
/// The import ends <c>checked</c> when no row has an error, and <c>failed</c> with every error otherwise. A row
/// error is keyed <c>/rows/{n}</c> followed by the pointer into the create body, <c>n</c> counting data rows
/// from 1. A problem of the header is keyed <c>/columns/{n}</c> and stops the check. More rows than
/// the limit is one error at <c>/rows</c>, and the rows after the limit are not read. An import that is gone or
/// no longer pending has nothing left to do, and neither has one whose entity the active release no longer has,
/// which fails at the empty pointer.
/// </summary>
internal sealed partial class RecordImportCheckHandler(
    ReleaseModelCache models,
    RecordImportSettings settings,
    SensitiveValues sensitive,
    ILoggerFactory loggerFactory,
    ILogger<RecordImportCheckHandler> logger) : IExternalWorkItemHandler
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
            await CheckAsync(connection, transaction, application, entity, new RecordActor(import.UploaderId, import.UploaderRoles), import.Key, import.Format, import.File ?? [], now, new RecordCipher(sensitive, item.TenantId), errors, cancellationToken);
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
    private async Task CheckAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel application,
        EntityModel entity,
        RecordActor actor,
        string? importKey,
        string format,
        byte[] file,
        DateTimeOffset now,
        RecordCipher cipher,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        IRecordImportFile reader = format == RecordImportFormats.Xlsx ? RecordImportXlsx.Open(file) : RecordImportCsv.Open(file);
        if (reader.HeaderError is { } headerError)
        {
            errors["/columns"] = [headerError];
            return;
        }

        if (reader.Header is not { } header)
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

        var key = importKey is null ? null : RecordImportKey.Create(importKey, columns);
        if (importKey is not null && key is null)
        {
            errors["/columns"] = ["Has no column for the key."];
            return;
        }

        var references = RecordImportReferences.Create(application, actor.Roles, columns);
        foreach (var row in reader.Rows())
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

            var (resolved, lookupErrors) = await references.ResolveAsync(connection, cells, cancellationToken);
            var (match, keyFailure) = key is null
                ? (null, null)
                : await key.FindAsync(connection, transaction, application, entity, actor, resolved, cipher, loggerFactory, cancellationToken);
            if (keyFailure is not null)
            {
                errors[prefix] = [RecordImportRefusals.Message(keyFailure, update: true)];
                Add(errors, prefix, lookupErrors);
                continue;
            }

            if (match is not null)
            {
                var update = await RecordCommand.CheckUpdateAsync(
                    connection,
                    transaction,
                    application,
                    entity,
                    match.Id,
                    actor,
                    columns.Body(resolved, key!.Index, match.Version),
                    now,
                    cipher,
                    loggerFactory,
                    cancellationToken);
                if (update?.Errors is { } updateErrors)
                {
                    Add(errors, prefix, updateErrors);
                }
                else if (update is not null)
                {
                    errors[prefix] = [RecordImportRefusals.Message(update, update: true)];
                }

                Add(errors, prefix, lookupErrors);
                continue;
            }

            var parse = RecordCommand.ParseCreate(application, entity, actor, columns.Body(resolved));
            if (parse.Failure is { } failure)
            {
                Add(errors, prefix, failure.Errors!);
                Add(errors, prefix, lookupErrors);
                continue;
            }

            if (RecordCommand.PrepareCreate(application, entity, actor, parse.Input!, form: null, now).Errors is { } prepareErrors)
            {
                Add(errors, prefix, prepareErrors);
            }

            Add(errors, prefix, lookupErrors);
        }
    }

    private static void Add(Dictionary<string, string[]> errors, string prefix, IDictionary<string, string[]>? rowErrors)
    {
        if (rowErrors is null)
        {
            return;
        }

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
