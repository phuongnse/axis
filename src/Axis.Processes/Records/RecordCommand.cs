using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Records;

/// <summary>The person or system a record command acts for: an id, a display name and roles.</summary>
public sealed record RecordActor(string Id, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>How a record command ended.</summary>
public enum RecordCommandOutcome
{
    /// <summary>A create or update wrote the record.</summary>
    Written,

    /// <summary>A delete removed the record.</summary>
    Deleted,

    /// <summary>The actor has no grant for the action.</summary>
    Denied,

    /// <summary>The actor has a grant, but the record is outside its record filters.</summary>
    OutsideFilters,

    /// <summary>The body sets a field that no grant for the action covers.</summary>
    UnwritableFields,

    /// <summary>The actor can reach no such record.</summary>
    NotFound,

    /// <summary>The body reader said the request is not JSON.</summary>
    UnsupportedContent,

    /// <summary>The body, a computed field, a validation or a reference is invalid.</summary>
    Invalid,

    /// <summary>The body names a version other than the stored one.</summary>
    StaleVersion,

    /// <summary>Storage refused the write, see <see cref="RecordCommandResult.Conflict"/>.</summary>
    Conflict,

    /// <summary>
    /// The active release is no longer the one the command was given, found under the start lock.
    /// Nothing was written. The caller resolves the active release again and runs the command again.
    /// </summary>
    ReleaseChanged,

    /// <summary>
    /// A record filter of the actor's grants reads a computed field that is being recomputed, so the access it
    /// decides cannot be checked. Nothing was written.
    /// </summary>
    PendingFilter,
}

/// <summary>What conflicted when the outcome is <see cref="RecordCommandOutcome.Conflict"/>.</summary>
public enum RecordConflict
{
    /// <summary>Another record references the record that a delete would remove.</summary>
    Referenced,

    /// <summary>A value that must be unique is taken.</summary>
    UniqueValue,

    /// <summary>The stored schema does not match the active model.</summary>
    Schema,

    /// <summary>A running or waiting process instance has the record as its subject.</summary>
    RunningProcess,
}

/// <summary>
/// The result of a record command. <see cref="Record"/> is the full stored record after a write,
/// whatever the actor may read of it. <see cref="Errors"/> hold the problems by JSON Pointer.
/// </summary>
public sealed record RecordCommandResult(
    RecordCommandOutcome Outcome,
    Record? Record = null,
    IDictionary<string, string[]>? Errors = null,
    RecordConflict? Conflict = null);

/// <summary>Reads the body of a write, or returns null when the request is not JSON.</summary>
public delegate Task<byte[]?> RecordBodyReader(CancellationToken cancellationToken);

/// <summary>
/// Creates, updates and deletes the records of an entity in the active release of an application
/// for an actor, with no HTTP request or session. The record API calls it, and so can any other
/// host. Every command is checked in order: the actor's grant for the entity and the action
/// (denied), then the stored record against the record filters (not found, outside the filters),
/// and only then the body. A <c>read</c> filter that reads a computed field that is being recomputed
/// is a pending filter, checked before the stored record, so before not found. An <c>update</c> or
/// <c>delete</c> filter that reads one is a pending filter too, checked after the stored record is
/// found and before it is outside the filters. The filter's result is then not used, because a stale
/// stored value can neither allow nor deny. A request that is not JSON is unsupported content, checked before any
/// of the body is read.
/// A grant with a record filter reaches only the records that match it, and the grants combine
/// with OR. An update or a delete of a record that matches no <c>read</c> filter, or by an actor
/// with no <c>read</c> grant, is not found. An update or a delete of a readable record that matches
/// no <c>update</c> or <c>delete</c> filter is outside the filters. A create or update whose record
/// as stored matches no <c>create</c> or <c>update</c> filter is outside the filters too: it is
/// checked in the write transaction, after storage, and rolls the write back. A filter that fails
/// for a record grants nothing through its grant and is logged.
/// A grant with <c>fields</c> covers only those fields.
/// A create or update writes the record and its child rows in one transaction. A create sets the
/// entity's creator fields to the actor's id. Every create, update and delete that succeeds appends
/// one audit record in its transaction. Its actor is the actor, with their display name. A write
/// that fails leaves none.
/// A create or update also starts the processes of the data change triggers that match it, in the
/// same transaction, as <c>system</c>. A write that fails starts none.
/// A create or update takes the application's start lock shared, as the first statement of its write
/// transaction. When the active release is no longer the given release, it writes nothing and the
/// outcome is <see cref="RecordCommandOutcome.ReleaseChanged"/>, so the caller can run it again on the
/// new release. A delete never starts a process. It takes the start lock shared first and then the record's
/// row lock. It is refused as a conflict when a running or waiting process instance has the record as its
/// subject, checked after the record filters.
/// A create or update may name a form of the entity. The form is checked after the grant and the
/// record checks and before the content type: a name that is no form of the entity is invalid.
/// A body is checked in order: content type, then body, then a field that no grant for the action
/// covers, then the computed fields of the record as it will be stored and of its rows, then the
/// form's <c>requiredWhen</c> conditions and the entity's validations on that record, then storage.
/// An update of an entity with computed fields or validations, or with a form that has
/// <c>requiredWhen</c> conditions, reads the stored record first, so an unknown record is not found there.
/// </summary>
public static class RecordCommand
{
    /// <summary>Creates a record of <paramref name="model"/> from the body that <paramref name="readBody"/> reads.</summary>
    public static async Task<RecordCommandResult> CreateAsync(
        NpgsqlConnection connection,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        EntityModel model,
        RecordActor actor,
        RecordBodyReader readBody,
        string? form,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var access = PolicyEvaluator.Access(application, actor.Roles, model, PolicyAction.Create);
        if (!access.Granted)
        {
            return new RecordCommandResult(RecordCommandOutcome.Denied);
        }

        FormModel? formModel = null;
        if (form is not null && !TryResolveForm(application, model, form, out formModel))
        {
            return Invalid(UnknownForm());
        }

        if (await readBody(cancellationToken) is not { } body)
        {
            return new RecordCommandResult(RecordCommandOutcome.UnsupportedContent);
        }

        var parsed = RecordInputParser.Parse(body, model, application, RecordOperation.Create);
        if (parsed.Errors is { } errors)
        {
            return Invalid(errors);
        }

        if (RecordFieldAccess.Unwritable(parsed.Input!, PolicyEvaluator.Fields(application, actor.Roles, model, PolicyAction.Create)) is { } unwritable)
        {
            return new RecordCommandResult(RecordCommandOutcome.UnwritableFields, Errors: unwritable);
        }

        // The creator fields are set before the computed fields and validations, so they see the
        // record as it will be stored.
        var input = parsed.Input!;
        var values = RecordCreator.WithCreator(model, input.Values, actor.Id);
        var computed = RecordComputer.ComputeCreate(application, model, values, input.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return Invalid(computeErrors);
        }

        RecordWriteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // The start lock comes first, before any entity table is touched in this transaction, so a
            // migrating activation cannot deadlock with this write. A create or update can start a process
            // whatever the release it was given declares, since the new release may add triggers.
            if (await StartLock.TakeSharedAsync(transaction, application.Manifest.Id, cancellationToken) != releaseId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RecordCommandResult(RecordCommandOutcome.ReleaseChanged);
            }

            // The validations run in the write transaction, so their now() is its start time.
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var failures = RecordFormRequirements.Merge(
                formModel is null ? null : RecordFormRequirements.Check(application, model, formModel, null, computed.Values, computed.Rows, now),
                RecordValidator.ValidateCreate(application, model, computed.Values, computed.Rows, now));
            if (failures is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Invalid(failures);
            }

            result = await RecordCommands.CreateAsync(connection, application, model, releaseId, computed.Values, computed.Rows, cancellationToken: cancellationToken);
            var written = result is { Outcome: RecordWriteOutcome.Written, Record: { } record } ? record : null;
            var outside = written is not null
                && !await WithinFiltersAsync(connection, transaction, application, model, written.Id, access, actor.Id, loggerFactory, cancellationToken);
            await CompleteAsync(
                transaction,
                written is not null && !outside
                    ? RecordAudit(actor, AuditActions.RecordCreated, application, model, written.Id, written.Version)
                    : null,
                () => DataChangeTriggerStarts.StartAsync(
                    transaction,
                    tenantId,
                    application,
                    releaseId,
                    model,
                    DataChangeKinds.Create,
                    written!,
                    changedFields: null,
                    loggerFactory.CreateLogger(typeof(DataChangeTriggerStarts)),
                    cancellationToken),
                cancellationToken);
            if (outside)
            {
                return new RecordCommandResult(RecordCommandOutcome.OutsideFilters);
            }
        }

        return WriteResult(result);
    }

    /// <summary>Updates the record with <paramref name="recordId"/> from the body that <paramref name="readBody"/> reads.</summary>
    public static async Task<RecordCommandResult> UpdateAsync(
        NpgsqlConnection connection,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        EntityModel model,
        Guid recordId,
        RecordActor actor,
        RecordBodyReader readBody,
        string? form,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var access = PolicyEvaluator.Access(application, actor.Roles, model, PolicyAction.Update);
        if (!access.Granted)
        {
            return new RecordCommandResult(RecordCommandOutcome.Denied);
        }

        // An update needs the stored record to be readable, then to match an update filter. Each
        // check gives the version it saw, which the body must name, so a write in between cannot
        // carry the record past them.
        var read = PolicyEvaluator.Access(application, actor.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return new RecordCommandResult(RecordCommandOutcome.NotFound);
        }

        var checkedVersions = new List<long>();
        if (read.Filters is { } readFilters)
        {
            if (await ReadsPendingAsync(connection, null, application, model, readFilters, loggerFactory, cancellationToken))
            {
                return new RecordCommandResult(RecordCommandOutcome.PendingFilter);
            }

            var match = await MatchAsync(connection, null, application, model, recordId, readFilters, actor.Id, loggerFactory, cancellationToken);
            if (match is not { Found: true, Matched: true })
            {
                return new RecordCommandResult(RecordCommandOutcome.NotFound);
            }

            checkedVersions.Add(match.Version);
        }

        if (access.Filters is { } updateFilters)
        {
            var match = await MatchAsync(connection, null, application, model, recordId, updateFilters, actor.Id, loggerFactory, cancellationToken);
            if (!match.Found)
            {
                return new RecordCommandResult(RecordCommandOutcome.NotFound);
            }

            if (await ReadsPendingAsync(connection, null, application, model, updateFilters, loggerFactory, cancellationToken))
            {
                return new RecordCommandResult(RecordCommandOutcome.PendingFilter);
            }

            if (!match.Matched)
            {
                return new RecordCommandResult(RecordCommandOutcome.OutsideFilters);
            }

            checkedVersions.Add(match.Version);
        }

        FormModel? formModel = null;
        if (form is not null && !TryResolveForm(application, model, form, out formModel))
        {
            return Invalid(UnknownForm());
        }

        if (await readBody(cancellationToken) is not { } body)
        {
            return new RecordCommandResult(RecordCommandOutcome.UnsupportedContent);
        }

        var parsed = RecordInputParser.Parse(body, model, application, RecordOperation.Update);
        if (parsed.Errors is { } errors)
        {
            return Invalid(errors);
        }

        if (RecordFieldAccess.Unwritable(parsed.Input!, PolicyEvaluator.Fields(application, actor.Roles, model, PolicyAction.Update)) is { } unwritable)
        {
            return new RecordCommandResult(RecordCommandOutcome.UnwritableFields, Errors: unwritable);
        }

        var input = parsed.Input!;
        if (checkedVersions.Any(version => version != input.Version))
        {
            return new RecordCommandResult(RecordCommandOutcome.StaleVersion);
        }

        // The stored record is read outside the write transaction. The client read its version
        // earlier, so a write that passes the version check finds the record as computed here and
        // validated below.
        Record? stored = null;
        if (model.Validations.Count > 0 || model.HasComputedFields || formModel?.HasRequiredWhen == true)
        {
            stored = await RecordQueries.GetAsync(connection, application, model, recordId, cancellationToken);
            if (stored is null)
            {
                return new RecordCommandResult(RecordCommandOutcome.NotFound);
            }
        }

        var computed = RecordComputer.ComputeUpdate(application, model, stored, input.Values, input.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return Invalid(computeErrors);
        }

        RecordWriteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // The start lock comes first, before any entity table is touched in this transaction, so a
            // migrating activation cannot deadlock with this write. A create or update can start a process
            // whatever the release it was given declares, since the new release may add triggers.
            if (await StartLock.TakeSharedAsync(transaction, application.Manifest.Id, cancellationToken) != releaseId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RecordCommandResult(RecordCommandOutcome.ReleaseChanged);
            }

            // The validations run in the write transaction, so their now() is its start time.
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            var failures = RecordFormRequirements.Merge(
                formModel is null ? null : RecordFormRequirements.Check(application, model, formModel, stored, computed.Values, computed.Rows, now),
                RecordValidator.ValidateUpdate(application, model, stored, computed.Values, computed.Rows, now));
            if (failures is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Invalid(failures);
            }

            // A trigger with fields compares the record before and after the write. The row lock
            // makes this read the version the update replaces.
            Record? before = null;
            if (DataChangeTriggerStarts.NeedsPreviousRecord(application, model)
                && await RecordCommands.LockAsync(connection, model, recordId, cancellationToken))
            {
                before = await RecordQueries.GetAsync(connection, application, model, recordId, cancellationToken);
            }

            result = await RecordCommands.UpdateAsync(connection, application, model, releaseId, recordId, input.Version!.Value, computed.Values, computed.Rows, cancellationToken);
            var written = result is { Outcome: RecordWriteOutcome.Written, Record: { } record } ? record : null;
            var outside = written is not null
                && !await WithinFiltersAsync(connection, transaction, application, model, written.Id, access, actor.Id, loggerFactory, cancellationToken);
            await CompleteAsync(
                transaction,
                written is not null && !outside
                    ? RecordAudit(actor, AuditActions.RecordUpdated, application, model, written.Id, written.Version, SetFields(model, input))
                    : null,
                () => DataChangeTriggerStarts.StartAsync(
                    transaction,
                    tenantId,
                    application,
                    releaseId,
                    model,
                    DataChangeKinds.Update,
                    written!,
                    before is null
                        ? null
                        : DataChangeTriggerStarts.ChangedFields(model, before, written!, input.Rows.Select(rows => rows.Collection.Name)),
                    loggerFactory.CreateLogger(typeof(DataChangeTriggerStarts)),
                    cancellationToken),
                cancellationToken);
            if (outside)
            {
                return new RecordCommandResult(RecordCommandOutcome.OutsideFilters);
            }
        }

        // The response shows a pending computed field as the active release computes it. A write that leaves
        // out the rows of a collection with computed fields stamps nothing, so the row can still be out of date.
        if (result is { Outcome: RecordWriteOutcome.Written, Record: { } updated })
        {
            result = result with { Record = await PendingComputedReads.ApplyAsync(connection, null, application, model, updated, cancellationToken) };
        }

        return WriteResult(result);
    }

    /// <summary>Deletes the record with <paramref name="recordId"/>.</summary>
    public static async Task<RecordCommandResult> DeleteAsync(
        NpgsqlConnection connection,
        ApplicationModel application,
        EntityModel model,
        Guid recordId,
        RecordActor actor,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var access = PolicyEvaluator.Access(application, actor.Roles, model, PolicyAction.Delete);
        if (!access.Granted)
        {
            return new RecordCommandResult(RecordCommandOutcome.Denied);
        }

        // A delete needs the stored record to be readable, then to match a delete filter.
        var read = PolicyEvaluator.Access(application, actor.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return new RecordCommandResult(RecordCommandOutcome.NotFound);
        }

        RecordDeleteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // A delete pins no release, so the released id is ignored. The lock keeps the lock order with a
            // migrating activation, and the record's row lock below orders the delete with a start.
            await StartLock.TakeSharedAsync(transaction, application.Manifest.Id, cancellationToken);

            // The row is locked first, so the filters and the instance check see the version that is deleted.
            if (read.Filters is { } pendingRead
                && await ReadsPendingAsync(connection, transaction, application, model, pendingRead, loggerFactory, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RecordCommandResult(RecordCommandOutcome.PendingFilter);
            }

            if (!await RecordCommands.LockAsync(connection, model, recordId, cancellationToken)
                || (read.Filters is { } readFilters
                    && await MatchAsync(connection, transaction, application, model, recordId, readFilters, actor.Id, loggerFactory, cancellationToken) is not { Found: true, Matched: true }))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RecordCommandResult(RecordCommandOutcome.NotFound);
            }

            if (access.Filters is { } pendingDelete)
            {
                if (await ReadsPendingAsync(connection, transaction, application, model, pendingDelete, loggerFactory, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new RecordCommandResult(RecordCommandOutcome.PendingFilter);
                }

                if (!await WithinFiltersAsync(connection, transaction, application, model, recordId, access, actor.Id, loggerFactory, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new RecordCommandResult(RecordCommandOutcome.OutsideFilters);
                }
            }

            // After the filters, so the refusal never shows a record the actor cannot reach. A separate
            // statement after the row lock, so it sees an instance that a start committed while this waited.
            if (await ProcessStarts.HasOpenInstanceAsync(transaction, application.Manifest.Id, model.Id, recordId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RecordCommandResult(RecordCommandOutcome.Conflict, Conflict: RecordConflict.RunningProcess);
            }

            result = await RecordCommands.DeleteAsync(connection, model, recordId, cancellationToken);
            await CompleteAsync(
                transaction,
                result is { Outcome: RecordDeleteOutcome.Deleted, Version: { } version }
                    ? RecordAudit(actor, AuditActions.RecordDeleted, application, model, recordId, version)
                    : null,
                beforeCommit: null,
                cancellationToken);
        }

        return result.Outcome switch
        {
            RecordDeleteOutcome.Deleted => new RecordCommandResult(RecordCommandOutcome.Deleted),
            RecordDeleteOutcome.NotFound => new RecordCommandResult(RecordCommandOutcome.NotFound),
            RecordDeleteOutcome.Referenced => new RecordCommandResult(RecordCommandOutcome.Conflict, Conflict: RecordConflict.Referenced),
            var outcome => throw new InvalidOperationException($"Unexpected delete outcome {outcome}."),
        };
    }

    /// <summary>
    /// Checks the record with <paramref name="recordId"/> against <paramref name="filters"/> for the
    /// actor with id <paramref name="actorId"/>, and logs the policy and grant of each filter that
    /// failed for it.
    /// </summary>
    public static async Task<RecordFilterMatch> MatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel application,
        EntityModel model,
        Guid recordId,
        IReadOnlyList<RecordFilter> filters,
        string actorId,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var match = await RecordFilters.MatchAsync(
            connection, transaction, application, model, recordId, Expressions(filters), actorId, cancellationToken);
        if (match.Failed.Count > 0)
        {
            var logger = loggerFactory.CreateLogger(typeof(RecordCommand).FullName!);
            foreach (var index in match.Failed)
            {
                RecordFilterLog.FilterFailed(logger, filters[index].Policy, filters[index].Grant, model.Name);
            }
        }

        return match;
    }

    /// <summary>
    /// Whether a filter of <paramref name="filters"/> reads a computed field that is being recomputed, in
    /// <paramref name="transaction"/> when it is not <see langword="null"/>. A path through reference fields
    /// reads a field of each entity on it. The pending state is read once per entity, not per record, so the
    /// answer does not tell whether any record exists. A line names the policy, the grant and the entity of
    /// each filter that does, never a value.
    /// </summary>
    public static async Task<bool> ReadsPendingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel application,
        EntityModel model,
        IReadOnlyList<RecordFilter> filters,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var read = filters.Select(filter => RecordFilters.FieldsRead(application, model, filter.Expression)).ToList();
        var entityIds = read.SelectMany(fields => fields.Select(field => field.Entity.Id)).Distinct().ToList();
        var pending = await PendingComputedFields.ReadAsync(connection, transaction, entityIds, cancellationToken);
        if (pending.Count == 0)
        {
            return false;
        }

        var refused = false;
        for (var index = 0; index < filters.Count; index++)
        {
            if (read[index].Any(field => pending.TryGetValue(field.Entity.Id, out var names) && names.Contains(field.Field)))
            {
                RecordFilterLog.FilterPending(loggerFactory.CreateLogger(typeof(RecordCommand).FullName!), filters[index].Policy, filters[index].Grant, model.Name);
                refused = true;
            }
        }

        return refused;
    }

    /// <summary>
    /// The declared names of the fields and child collections an update body sets, in declaration
    /// order. A field sent with its stored value is listed too, and a computed field never is.
    /// </summary>
    public static IEnumerable<string> SetFields(EntityModel model, RecordInput input)
    {
        var named = input.Values.Select(value => value.Field.Name)
            .Concat(input.Rows.Select(rows => rows.Collection.Name))
            .ToHashSet(StringComparer.Ordinal);
        return model.Fields.Select(field => field.Name).Where(named.Contains);
    }

    /// <summary>The filter expressions of <paramref name="filters"/>, in order.</summary>
    public static List<ExpressionModel> Expressions(IReadOnlyList<RecordFilter> filters) =>
        [.. filters.Select(filter => filter.Expression)];

    private static RecordCommandResult Invalid(IDictionary<string, string[]> errors) =>
        new(RecordCommandOutcome.Invalid, Errors: errors);

    // The form must be one of the entity's forms. A repeated or empty name never names one.
    private static bool TryResolveForm(ApplicationModel application, EntityModel model, string name, out FormModel? form)
    {
        form = application.TryGetForm(name, out var found) && found.Entity.Id == model.Id ? found : null;
        return form is not null;
    }

    private static Dictionary<string, string[]> UnknownForm() =>
        new() { ["form"] = ["Must name a form of this entity."] };

    // Maps a storage result that is not a success. The conflicts name no column or constraint:
    // the storage names are not part of the outcome.
    private static RecordCommandResult WriteResult(RecordWriteResult result) =>
        result.Outcome switch
        {
            RecordWriteOutcome.Written => new RecordCommandResult(RecordCommandOutcome.Written, result.Record),
            RecordWriteOutcome.NotFound => new RecordCommandResult(RecordCommandOutcome.NotFound),
            RecordWriteOutcome.StaleVersion => new RecordCommandResult(RecordCommandOutcome.StaleVersion),
            RecordWriteOutcome.MissingReference or RecordWriteOutcome.Invalid => Invalid(result.Errors!),
            RecordWriteOutcome.UniqueViolation => new RecordCommandResult(
                RecordCommandOutcome.Conflict, Errors: result.Errors, Conflict: RecordConflict.UniqueValue),
            RecordWriteOutcome.SchemaConflict => new RecordCommandResult(RecordCommandOutcome.Conflict, Conflict: RecordConflict.Schema),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unexpected write outcome."),
        };

    /// <summary>
    /// Whether the record with <paramref name="recordId"/> matches a filter of
    /// <paramref name="access"/>, or <see langword="true"/> when it reaches every record. Run it
    /// inside <paramref name="transaction"/> to check a record as the transaction wrote it.
    /// </summary>
    private static async Task<bool> WithinFiltersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel application,
        EntityModel model,
        Guid recordId,
        RecordAccess access,
        string actorId,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        access.Filters is not { } filters
        || (await MatchAsync(connection, transaction, application, model, recordId, filters, actorId, loggerFactory, cancellationToken)).Matched;

    /// <summary>
    /// Appends <paramref name="audit"/>, runs <paramref name="beforeCommit"/> and commits the write
    /// when it succeeded, which is when there is an audit record, and rolls back every write
    /// otherwise. Disposing the transaction rolls back on an exception, so a write whose audit
    /// record or trigger start fails is not committed.
    /// </summary>
    private static async Task CompleteAsync(
        NpgsqlTransaction transaction,
        AuditEntry? audit,
        Func<Task>? beforeCommit,
        CancellationToken cancellationToken)
    {
        if (audit is not null)
        {
            await AuditRecords.AppendAsync(transaction, audit, cancellationToken);
            if (beforeCommit is not null)
            {
                await beforeCommit();
            }

            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The audit record of a record write. Its details hold the record's version, and on update
    /// the names of the fields the body sets. They never hold a field value. Its actor is
    /// <paramref name="actor"/>, with their display name.
    /// </summary>
    private static AuditEntry RecordAudit(
        RecordActor actor,
        string action,
        ApplicationModel application,
        EntityModel model,
        Guid recordId,
        long version,
        IEnumerable<string>? fields = null)
    {
        var details = new JsonObject { ["version"] = version };
        if (fields is not null)
        {
            details["fields"] = new JsonArray([.. fields.Select(name => JsonValue.Create(name))]);
        }

        return new AuditEntry(
            actor.Id,
            action,
            application.Manifest.Id,
            model.Id,
            recordId,
            ProcessInstanceId: null,
            details,
            actor.DisplayName);
    }
}
