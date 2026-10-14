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
/// create. The triggers come from the model of the release the request resolved, in model order,
/// and each start runs as <c>system</c>, pinned to that release, so the writer needs no
/// <c>start</c> grant. A trigger whose <c>on</c> does not list the write, or whose <c>fields</c>
/// lists no changed field, is passed over without a log line. A <c>when</c> or start condition that
/// is not true, or a process that already runs for the record, skips the start and logs it at
/// Information level. Any other error throws, so the caller's transaction does not commit.
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
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(logger);

        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
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

            if (trigger.When is { } when)
            {
                var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
                var evaluated = await RecordExpressions.EvaluateAsync(connection, application, entity, written, when, now, cancellationToken);
                if (evaluated.Value is not true)
                {
                    LogSkipped(
                        logger,
                        trigger.Name,
                        trigger.Process.Name,
                        written.Id,
                        evaluated.Error is { } error ? $"the when failed: {Clause(error.Message)}" : "the when is not true");
                    continue;
                }
            }

            var process = application.Processes.FirstOrDefault(candidate => candidate.Id == trigger.Process.Id)
                ?? throw new InvalidOperationException($"The release has no process '{trigger.Process.Name}'.");
            var subject = application.FindEntity(process.Entity.Id)
                ?? throw new InvalidOperationException($"The release has no entity '{process.Entity.Name}'.");
            var started = await ProcessStarter.StartAsync(
                connection,
                transaction,
                tenantId,
                application,
                releaseId,
                process,
                subject,
                written.Id,
                written,
                Guid.CreateVersion7(),
                AuditActors.System,
                actorName: null,
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
                        : started.Error is { } error
                            ? $"the start condition failed: {Clause(error)}"
                            : "the start condition is not true");
            }
        }
    }

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId}: {Reason}.")]
    private static partial void LogSkipped(ILogger logger, string trigger, string process, Guid subjectId, string reason);

    /// <summary>An error message without its closing period, to fit inside the log line.</summary>
    private static string Clause(string message) => message.TrimEnd('.');
}
