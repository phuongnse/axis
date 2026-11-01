using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Starts the processes of data change triggers for a record write, in the transaction that wrote
/// the record. The record API calls it after a create or update, and an inbound endpoint after its
/// create. A delete trigger starts a process without a subject, with parameters worked out from the
/// record as it was before the delete: <see cref="PrepareDeleteAsync"/> evaluates the <c>when</c> and
/// the parameters before the row is deleted, and <see cref="StartAfterDeleteAsync"/> starts the
/// processes after the audit record. The triggers come from the model of the release the request resolved, in model order,
/// and each start runs as <c>system</c>, pinned to that release, so the writer needs no
/// <c>start</c> grant. The instance takes the accountable user of the write context, and no deciding task. A trigger whose <c>on</c> does not list the write, or whose <c>fields</c>
/// lists no changed field, is passed over without a log line. A <c>when</c> or start condition that
/// is not true, a parameter that fails or is null, or a process that already runs for the record or,
/// for a delete trigger, at all, skips the start and logs it at Information level. Any other error throws, so the caller's transaction does not commit.
/// </summary>
public static partial class DataChangeTriggerStarts
{
    /// <summary>
    /// Whether the entity has an update trigger with <c>fields</c>, so a caller must read the record
    /// before it updates it to tell which fields changed.
    /// </summary>
    public static bool NeedsPreviousRecord(ApplicationModel application, EntityModel entity)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);

        return application.Triggers.Any(trigger =>
            trigger.Type == TriggerType.DataChange
            && trigger.Entity?.Id == entity.Id
            && trigger.On.HasFlag(DataChangeKinds.Update)
            && trigger.Fields is not null);
    }

    /// <summary>
    /// The declared names of the fields whose stored value differs between the two versions of the
    /// record, plus every collection in <paramref name="replacedCollections"/>. An update replaces
    /// all rows of a collection it names, so their stored row ids change and comparing them says
    /// nothing.
    /// </summary>
    public static IReadOnlySet<string> ChangedFields(
        EntityModel entity,
        Record before,
        Record after,
        IEnumerable<string> replacedCollections)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(replacedCollections);

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in entity.Fields.Where(field => field.HasColumn))
        {
            before.Values.TryGetValue(field.Name, out var previous);
            after.Values.TryGetValue(field.Name, out var current);
            if (!JsonNode.DeepEquals(previous, current))
            {
                changed.Add(field.Name);
            }
        }

        changed.UnionWith(replacedCollections);
        return changed;
    }

    /// <summary>
    /// Starts the update triggers of the active release for a record that a system-side write
    /// changed in <paramref name="transaction"/>. The write used the release <paramref name="writeReleaseId"/>,
    /// and <paramref name="entity"/>, <paramref name="before"/> and <paramref name="written"/> come from its
    /// model. The triggers, the <c>when</c> and the starts use the active release, so when the two
    /// differ the record is read again with the active model. Returns at once when the active
    /// release has no such entity or no update trigger on it.
    /// </summary>
    public static async Task StartAfterUpdateAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        Guid writeReleaseId,
        EntityModel entity,
        Record before,
        Record written,
        IEnumerable<string> replacedCollections,
        RecordWriteContext write,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(replacedCollections);
        ArgumentNullException.ThrowIfNull(write);

        if (active.FindEntity(entity.Id) is not { } watched
            || !active.Triggers.Any(trigger =>
                trigger.Type == TriggerType.DataChange
                && trigger.Entity?.Id == watched.Id
                && trigger.On.HasFlag(DataChangeKinds.Update)))
        {
            return;
        }

        var changed = ChangedFields(entity, before, written, replacedCollections);
        var record = written;
        if (activeReleaseId != writeReleaseId)
        {
            var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
            record = await RecordQueries.GetAsync(connection, active, watched, written.Id, cancellationToken)
                ?? throw new InvalidOperationException($"No record of '{watched.Name}' has the id '{written.Id}'.");
        }

        await StartAsync(transaction, tenantId, active, activeReleaseId, watched, DataChangeKinds.Update, record, changed, write, logger, cancellationToken);
    }

    /// <summary>
    /// Starts the create triggers of the active release for a record that a system-side write
    /// created in <paramref name="transaction"/>. The write used the release <paramref name="writeReleaseId"/>,
    /// and <paramref name="entity"/> and <paramref name="written"/> come from its model. The
    /// triggers, the <c>when</c> and the starts use the active release, so when the two differ the
    /// record is read again with the active model. Returns at once when the active release has no
    /// such entity or no create trigger on it.
    /// </summary>
    public static async Task StartAfterCreateAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        Guid writeReleaseId,
        EntityModel entity,
        Record written,
        RecordWriteContext write,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(write);

        if (active.FindEntity(entity.Id) is not { } watched
            || !active.Triggers.Any(trigger =>
                trigger.Type == TriggerType.DataChange
                && trigger.Entity?.Id == watched.Id
                && trigger.On.HasFlag(DataChangeKinds.Create)))
        {
            return;
        }

        var record = written;
        if (activeReleaseId != writeReleaseId)
        {
            var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
            record = await RecordQueries.GetAsync(connection, active, watched, written.Id, cancellationToken)
                ?? throw new InvalidOperationException($"No record of '{watched.Name}' has the id '{written.Id}'.");
        }

        await StartAsync(transaction, tenantId, active, activeReleaseId, watched, DataChangeKinds.Create, record, changedFields: null, write, logger, cancellationToken);
    }

    /// <summary>
    /// Starts the process of each data change trigger on <paramref name="entity"/> that lists
    /// <paramref name="kind"/> and passes its <c>fields</c>, for the record <paramref name="written"/>
    /// in <paramref name="transaction"/>. <paramref name="changedFields"/> is null for a create,
    /// which passes every <c>fields</c>. The record's row is locked by the write, so the starts do
    /// not lock it again.
    /// </summary>
    public static async Task StartAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        EntityModel entity,
        DataChangeKinds kind,
        Record written,
        IReadOnlySet<string>? changedFields,
        RecordWriteContext write,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(logger);

        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));

        // The when and the start condition read the record with its pending computed fields computed by the
        // release the triggers come from. It is worked out once, at the first trigger that needs it. The changed
        // fields and the id stay on the written record.
        Record? computed = null;
        foreach (var trigger in application.Triggers)
        {
            if (trigger.Type != TriggerType.DataChange
                || trigger.Entity?.Id != entity.Id
                || !trigger.On.HasFlag(kind)
                || (kind == DataChangeKinds.Update
                    && trigger.Fields is { } fields
                    && !fields.Any(field => changedFields?.Contains(field) == true)))
            {
                continue;
            }

            computed ??= await PendingComputedReads.ApplyAsync(connection, transaction, application, entity, written, cancellationToken);
            if (trigger.When is { } when)
            {
                var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
                var evaluated = await RecordExpressions.EvaluateAsync(connection, application, entity, computed, when, now, cancellationToken);
                if (evaluated.Value is not true)
                {
                    LogSkipped(
                        logger,
                        trigger.Name,
                        trigger.Process.Name,
                        written.Id,
                        evaluated.Error is { } error ? $"the when failed: {error.Kind}" : "the when is not true");
                    continue;
                }
            }

            var process = application.Processes.FirstOrDefault(candidate => candidate.Id == trigger.Process.Id)
                ?? throw new InvalidOperationException($"The release has no process '{trigger.Process.Name}'.");
            // The compiler refuses a trigger that names a process without a subject.
            var subjectReference = process.Entity
                ?? throw new InvalidOperationException($"The process '{process.Name}' has no subject, so a trigger cannot start it.");
            var subject = application.FindEntity(subjectReference.Id)
                ?? throw new InvalidOperationException($"The release has no entity '{subjectReference.Name}'.");
            var started = await ProcessStarter.StartAsync(
                connection,
                transaction,
                tenantId,
                application,
                releaseId,
                process,
                subject,
                written.Id,
                computed,
                Guid.CreateVersion7(),
                AuditActors.System,
                write.AccountableUser,
                new JsonObject { ["trigger"] = trigger.Name },
                cancellationToken);
            if (started.Outcome != ProcessStartOutcome.Started)
            {
                LogSkipped(
                    logger,
                    trigger.Name,
                    process.Name,
                    written.Id,
                    started.Outcome == ProcessStartOutcome.AlreadyRunning
                        ? "the process already runs for the record"
                        : started.ErrorKind is { } errorKind
                            ? $"the start condition failed: {errorKind}"
                            : "the start condition is not true");
            }
        }
    }

    /// <summary>One delete trigger that fired: the process to start with its parameters, or why the start is skipped.</summary>
    public sealed record PreparedDeleteStart(TriggerModel Trigger, ProcessModel Process, JsonObject? Parameters, string? SkipReason);

    /// <summary>
    /// Works out the delete triggers of <paramref name="application"/> for the record with
    /// <paramref name="recordId"/>, which the caller has locked in <paramref name="transaction"/> and has not
    /// deleted yet. Each trigger's <c>when</c> and parameters are evaluated over the record as it is now, with
    /// its child rows and pending computed fields, since they are gone once the row is deleted. Returns an
    /// empty list at once when the entity has no delete trigger. A trigger whose <c>when</c> is not true, or
    /// whose <c>when</c> or a parameter fails or gives null, has no parameters and a reason to skip.
    /// </summary>
    public static async Task<IReadOnlyList<PreparedDeleteStart>> PrepareDeleteAsync(
        NpgsqlTransaction transaction,
        ApplicationModel application,
        EntityModel entity,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);

        var triggers = application.Triggers
            .Where(trigger =>
                trigger.Type == TriggerType.DataChange
                && trigger.Entity?.Id == entity.Id
                && trigger.On.HasFlag(DataChangeKinds.Delete))
            .ToList();
        if (triggers.Count == 0)
        {
            return [];
        }

        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
        var stored = await RecordQueries.GetAsync(connection, application, entity, recordId, cancellationToken)
            ?? throw new InvalidOperationException($"No record of '{entity.Name}' has the id '{recordId}'.");
        var record = await PendingComputedReads.ApplyAsync(connection, transaction, application, entity, stored, cancellationToken);
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var prepared = new List<PreparedDeleteStart>();
        foreach (var trigger in triggers)
        {
            var process = application.Processes.FirstOrDefault(candidate => candidate.Id == trigger.Process.Id)
                ?? throw new InvalidOperationException($"The release has no process '{trigger.Process.Name}'.");
            if (trigger.When is { } when)
            {
                var evaluated = await RecordExpressions.EvaluateAsync(connection, application, entity, record, when, now, cancellationToken);
                if (evaluated.Value is not true)
                {
                    prepared.Add(new PreparedDeleteStart(
                        trigger,
                        process,
                        null,
                        evaluated.Error is { } error ? $"the when failed: {error.Kind}" : "the when is not true"));
                    continue;
                }
            }

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            string? skip = null;
            foreach (var (name, expression) in trigger.ParameterExpressions ?? new Dictionary<string, ExpressionModel>())
            {
                var evaluated = await RecordExpressions.EvaluateAsync(connection, application, entity, record, expression, now, cancellationToken);
                if (evaluated.Error is { } error)
                {
                    skip = $"the parameter {name} failed: {error.Kind}";
                    break;
                }

                if (evaluated.Value is null)
                {
                    skip = $"the parameter {name} is null or does not fit";
                    break;
                }

                values[name] = evaluated.Value;
            }

            // A process parameter has no optional flag, so a null value or one of another type skips the start.
            var parameters = skip is null ? ProcessParameters.ToJson(process, values) : null;
            skip ??= parameters is null ? "a parameter does not fit its type" : null;
            prepared.Add(new PreparedDeleteStart(trigger, process, skip is null ? parameters : null, skip));
        }

        return prepared;
    }

    /// <summary>
    /// Starts the process of each prepared delete trigger in <paramref name="transaction"/>, as <c>system</c>
    /// and pinned to <paramref name="releaseId"/>, for the record that <paramref name="recordId"/> names. A
    /// trigger with a reason to skip, a process that already runs and a start condition that is not true
    /// are logged at Information level and start nothing.
    /// </summary>
    public static async Task StartAfterDeleteAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        Guid releaseId,
        Guid recordId,
        IReadOnlyList<PreparedDeleteStart> prepared,
        RecordWriteContext write,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(logger);

        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
        foreach (var (trigger, process, parameters, skipReason) in prepared)
        {
            if (parameters is null)
            {
                LogSkipped(logger, trigger.Name, process.Name, recordId, skipReason ?? "the parameters are missing");
                continue;
            }

            var started = await ProcessStarter.StartWithParametersAsync(
                connection,
                transaction,
                tenantId,
                application,
                releaseId,
                process,
                parameters,
                Guid.CreateVersion7(),
                AuditActors.System,
                write.AccountableUser,
                new JsonObject { ["trigger"] = trigger.Name },
                cancellationToken);
            if (started.Outcome != ProcessStartOutcome.Started)
            {
                LogSkipped(
                    logger,
                    trigger.Name,
                    process.Name,
                    recordId,
                    started.Outcome == ProcessStartOutcome.AlreadyRunning
                        ? "the process already runs"
                        : started.ErrorKind is { } errorKind
                            ? $"the start condition failed: {errorKind}"
                            : "the start condition is not true");
            }
        }
    }

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId}: {Reason}.")]
    private static partial void LogSkipped(ILogger logger, string trigger, string process, Guid subjectId, string reason);
}
