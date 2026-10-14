using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs a <c>trigger.fanOut</c> work item: one occurrence of a schedule trigger. In one transaction
/// it extends the lease, reads the ids of the records the trigger's data source returns as
/// <c>system</c>, with the trigger's fixed parameters and in record id order, and starts the
/// trigger's process for each. The trigger and the data source come from the release the item
/// names, which was active at the occurrence. The process, the records and the pin come from the
/// release that is active now. With no active release, or one without the process, the occurrence
/// starts nothing. Each record gets a receipt in <c>axis.trigger_starts</c> keyed by the trigger,
/// the occurrence and the record, so a run again after a crash starts nothing twice. A start marks
/// the receipt <c>started</c>, and a skip leaves it <c>skipped</c> and is logged with its reason after the commit.
/// A data error in the data source's filter ends the occurrence with a warning.
/// </summary>
internal sealed partial class TriggerFanOutHandler(ReleaseModelCache models, ILogger<TriggerFanOutHandler> logger) : IExternalWorkItemHandler
{
    private const int PageSize = 100;

    private const string Savepoint = "fanout";

    public string Kind => ProcessStarts.TriggerFanOutWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        var triggerId = item.TriggerId ?? throw new InvalidOperationException($"The work item '{item.Id}' names no trigger.");
        var releaseId = item.ReleaseId ?? throw new InvalidOperationException($"The work item '{item.Id}' names no release.");
        var occurrenceAt = item.OccurrenceAt ?? throw new InvalidOperationException($"The work item '{item.Id}' names no occurrence.");
        var occurrence = occurrenceAt.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

        var skips = new List<(Guid SubjectId, string Reason)>();
        TriggerModel trigger;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, item.Lease, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, triggerId, occurrence);
                return WorkItemOutcome.LostClaim;
            }

            var context = new WorkItemContext(item, connection, transaction);
            var stored = await models.GetAsync(context, releaseId, cancellationToken);
            trigger = stored.Triggers.First(candidate => candidate.Id == triggerId);
            var dataSource = stored.DataSources.First(candidate => candidate.Id == trigger.DataSource!.Id);
            var active = await models.FindActiveAsync(context, stored.Manifest.Id, cancellationToken);
            var process = active?.Model.Processes.FirstOrDefault(candidate => candidate.Id == trigger.Process.Id);
            if (active is not { } current || process is null)
            {
                LogNothingToStart(trigger.Name, occurrence, trigger.Process.Name);
            }
            else
            {
                var parameters = dataSource.Parameters
                    .Select(parameter => new DataSourceParameterValue(parameter, trigger.Parameters!.GetValueOrDefault(parameter.Name)))
                    .ToList();
                var ids = await ListIdsAsync(connection, transaction, stored, dataSource, parameters, cancellationToken);
                if (ids is null)
                {
                    LogDataError(trigger.Name, occurrence);
                }
                else
                {
                    var entity = current.Model.FindEntity(process.Entity.Id)
                        ?? throw new InvalidOperationException($"The release has no entity '{process.Entity.Name}'.");
                    foreach (var id in ids)
                    {
                        if (await StartAsync(item, transaction, current.Model, current.ReleaseId, trigger, process, entity, occurrence, id, cancellationToken) is { } skipped)
                        {
                            skips.Add((id, skipped));
                        }
                    }
                }
            }

            if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, triggerId, occurrence);
                return WorkItemOutcome.LostClaim;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var (subjectId, reason) in skips)
        {
            LogSkipped(trigger.Name, trigger.Process.Name, subjectId, occurrence, reason);
        }

        return WorkItemOutcome.Completed;
    }

    /// <summary>
    /// Reads the root record ids of the data source, page by page. Returns <see langword="null"/>
    /// when the database rejects the filter with a data exception. That aborts the statement, so
    /// the read runs in a savepoint that is rolled back, which keeps the transaction usable.
    /// </summary>
    private static async Task<List<Guid>?> ListIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel model,
        DataSourceModel dataSource,
        IReadOnlyList<DataSourceParameterValue> parameters,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        await transaction.SaveAsync(Savepoint, cancellationToken);
        for (var page = 1; ; page++)
        {
            var rows = await DataSourceQueries.ListAsync(connection, model, dataSource, page, PageSize, sort: null, parameters, cancellationToken: cancellationToken);
            if (rows is null)
            {
                await transaction.RollbackAsync(Savepoint, cancellationToken);
                await transaction.ReleaseAsync(Savepoint, cancellationToken);
                return null;
            }

            ids.AddRange(rows.Items.Select(row => row.Id!.Value));
            if (rows.Items.Count < PageSize)
            {
                await transaction.ReleaseAsync(Savepoint, cancellationToken);
                return ids;
            }
        }
    }

    /// <summary>
    /// Inserts the record's receipt, then starts the trigger's process for it. Returns the reason the
    /// start was skipped, or <see langword="null"/> when it happened.
    /// </summary>
    private static async Task<string?> StartAsync(
        ClaimedWorkItem item,
        NpgsqlTransaction transaction,
        ApplicationModel active,
        Guid activeReleaseId,
        TriggerModel trigger,
        ProcessModel process,
        EntityModel entity,
        string occurrence,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));

        // The receipt goes first, so a concurrent or repeated run waits on its key and then skips.
        if (!await TriggerStarts.TryInsertAsync(transaction, trigger.Id, occurrence, subjectId, cancellationToken))
        {
            return "the record was already handled for the occurrence";
        }

        // The lock makes the read below and the start condition see the latest version, and a
        // concurrent write of the record waits until this start commits.
        if (!await RecordCommands.LockAsync(connection, entity, subjectId, cancellationToken)
            || await RecordQueries.GetAsync(connection, active, entity, subjectId, cancellationToken) is not { } record)
        {
            return "the record no longer exists";
        }

        var instanceId = Guid.CreateVersion7();
        var started = await ProcessStarter.StartAsync(
            connection,
            transaction,
            item.TenantId,
            active,
            activeReleaseId,
            process,
            entity,
            subjectId,
            record,
            instanceId,
            AuditActors.System,
            actorName: null,
            new JsonObject { ["trigger"] = trigger.Name, ["occurrenceAt"] = occurrence },
            cancellationToken);
        if (started.Outcome == ProcessStartOutcome.Started)
        {
            await TriggerStarts.MarkStartedAsync(transaction, trigger.Id, occurrence, subjectId, instanceId, cancellationToken);
            return null;
        }

        return started.Outcome == ProcessStartOutcome.AlreadyRunning
            ? "the process already runs for the record"
            : started.Error is { } error ? $"the start condition failed: {error.TrimEnd('.')}" : "the start condition is not true";
    }

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId} of occurrence {OccurrenceAt}: {Reason}.")]
    private partial void LogSkipped(string trigger, string process, Guid subjectId, string occurrenceAt, string reason);

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} started nothing for occurrence {OccurrenceAt}: the application has no active release with process {Process}.")]
    private partial void LogNothingToStart(string trigger, string occurrenceAt, string process);

    [LoggerMessage(LogLevel.Warning, "Trigger {Trigger} started nothing for occurrence {OccurrenceAt}: the database rejected the data source's filter with a data exception.")]
    private partial void LogDataError(string trigger, string occurrenceAt);

    [LoggerMessage(LogLevel.Warning, "Fan-out work item {ItemId} for trigger {TriggerId} and occurrence {OccurrenceAt} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid triggerId, string occurrenceAt);
}
