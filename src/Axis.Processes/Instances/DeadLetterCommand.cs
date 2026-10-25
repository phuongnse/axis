using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Processes.Work;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>How a redeliver or a discard ended.</summary>
public enum DeadLetterOutcome
{
    /// <summary>The dead letter is settled. The caller commits.</summary>
    Settled,

    /// <summary>The process has no dead letter with this id.</summary>
    NotFound,

    /// <summary>The dead letter is not <c>open</c>.</summary>
    NotOpen,

    /// <summary>The trigger of the dead letter is not in the active release, so nothing could deliver to it.</summary>
    TriggerGone,

    /// <summary>The kind of the dead letter cannot be redelivered yet.</summary>
    NotRedeliverable,
}

/// <summary>The result of a redeliver or a discard. <see cref="DeadLetter"/> is the settled row for <see cref="DeadLetterOutcome.Settled"/>.</summary>
public sealed record DeadLetterResult(DeadLetterOutcome Outcome, DeadLetterRow? DeadLetter = null);

/// <summary>
/// Redelivers or discards one <c>open</c> dead letter of a process, in the caller's transaction. The
/// user's <c>retry</c> grant is the caller's. The row is locked first. A redeliver of an
/// <c>event.deliver</c> dead letter needs its <c>async</c> trigger in the active release, clears the
/// target's failure count and its <c>deadLettered</c> receipt, and queues an <c>event.deliver</c> item that names the
/// trigger, so the handler delivers to that trigger alone. A redeliver of a <c>trigger.fanOut</c> dead
/// letter needs its schedule trigger in the active release, clears the occurrence's failure count and queues
/// the fan-out on the active release. Any other kind cannot be redelivered. A discard closes a dead letter
/// of any kind. Both set the state, the reason, the user and the time, and append the audit record
/// <c>deadLetter.redelivered</c> or <c>deadLetter.discarded</c>. The command never commits or rolls back.
/// A result other than <see cref="DeadLetterOutcome.Settled"/> wrote nothing, but the row may be locked.
/// </summary>
public static class DeadLetterCommand
{
    /// <param name="active">The active release's model.</param>
    /// <param name="activeReleaseId">The id of the active release.</param>
    /// <param name="processId">The process of the route. The dead letter must belong to it.</param>
    public static Task<DeadLetterResult> RedeliverAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        Guid processId,
        Guid id,
        string userId,
        string userName,
        string reason,
        CancellationToken cancellationToken) =>
        SettleAsync(connection, transaction, tenantId, active, activeReleaseId, processId, id, userId, userName, reason, redeliver: true, cancellationToken);

    /// <param name="active">The active release's model.</param>
    /// <param name="processId">The process of the route. The dead letter must belong to it.</param>
    public static Task<DeadLetterResult> DiscardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid processId,
        Guid id,
        string userId,
        string userName,
        string reason,
        CancellationToken cancellationToken) =>
        SettleAsync(connection, transaction, tenantId, active, Guid.Empty, processId, id, userId, userName, reason, redeliver: false, cancellationToken);

    private static async Task<DeadLetterResult> SettleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        Guid processId,
        Guid id,
        string userId,
        string userName,
        string reason,
        bool redeliver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(active);

        var applicationId = active.Manifest.Id;
        var letter = await LockAsync(connection, transaction, applicationId, processId, id, cancellationToken);
        if (letter is null)
        {
            return new DeadLetterResult(DeadLetterOutcome.NotFound);
        }

        if (letter.State != DeadLetters.Open)
        {
            return new DeadLetterResult(DeadLetterOutcome.NotOpen);
        }

        if (redeliver)
        {
            var refused = letter.Kind switch
            {
                DeliveryFailures.EventDeliverKind => await RedeliverEventAsync(connection, transaction, tenantId, active, letter, cancellationToken),
                DeliveryFailures.TriggerFanOutKind => await RedeliverFanOutAsync(connection, transaction, tenantId, active, activeReleaseId, letter, cancellationToken),
                _ => DeadLetterOutcome.NotRedeliverable,
            };
            if (refused != DeadLetterOutcome.Settled)
            {
                return new DeadLetterResult(refused);
            }
        }

        var settled = await UpdateAsync(
            transaction, id, redeliver ? DeadLetters.Redelivered : DeadLetters.Discarded, reason, userId, cancellationToken);
        var details = new JsonObject
        {
            ["deadLetterId"] = letter.Id,
            ["kind"] = letter.Kind,
            ["triggerId"] = letter.TriggerId,
            ["eventId"] = letter.EventId,
            ["occurrenceAt"] = letter.OccurrenceAt is { } occurrence ? OccurrenceText(occurrence) : null,
            ["reason"] = reason,
        };
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                userId,
                redeliver ? AuditActions.DeadLetterRedelivered : AuditActions.DeadLetterDiscarded,
                applicationId,
                null,
                null,
                null,
                details,
                userName),
            cancellationToken);
        return new DeadLetterResult(DeadLetterOutcome.Settled, settled);
    }

    private static async Task<DeadLetterOutcome> RedeliverEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        DeadLetterRow letter,
        CancellationToken cancellationToken)
    {
        if (letter.TriggerId is not { } triggerId || letter.EventId is not { } eventId)
        {
            return DeadLetterOutcome.NotRedeliverable;
        }

        // Otherwise the dead letter would be marked redelivered while the worker skips it.
        if (!active.Triggers.Any(trigger => trigger.Id == triggerId && trigger.Type == TriggerType.Event && trigger.Mode == TriggerMode.Async))
        {
            return DeadLetterOutcome.TriggerGone;
        }

        await DeleteFailureAsync(transaction, DeliveryFailures.EventDeliverKind, $"{triggerId:D}/{eventId:D}", cancellationToken);
        await using (var receipt = new NpgsqlCommand(
            "DELETE FROM axis.trigger_starts WHERE trigger_id = @trigger AND source = @source AND outcome = @outcome",
            connection,
            transaction))
        {
            receipt.Parameters.AddWithValue("trigger", triggerId);
            receipt.Parameters.AddWithValue("source", eventId.ToString("D"));
            receipt.Parameters.AddWithValue("outcome", TriggerStarts.DeadLettered);
            await receipt.ExecuteNonQueryAsync(cancellationToken);
        }

        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.EventDeliverWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            eventId: eventId,
            triggerId: triggerId);
        return DeadLetterOutcome.Settled;
    }

    private static async Task<DeadLetterOutcome> RedeliverFanOutAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        DeadLetterRow letter,
        CancellationToken cancellationToken)
    {
        if (letter.TriggerId is not { } triggerId || letter.OccurrenceAt is not { } occurrenceAt)
        {
            return DeadLetterOutcome.NotRedeliverable;
        }

        if (!active.Triggers.Any(trigger => trigger.Id == triggerId && trigger.DataSource is not null))
        {
            return DeadLetterOutcome.TriggerGone;
        }

        await DeleteFailureAsync(transaction, DeliveryFailures.TriggerFanOutKind, $"{triggerId:D}/{OccurrenceText(occurrenceAt)}", cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.TriggerFanOutWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            triggerId: triggerId,
            releaseId: activeReleaseId,
            occurrenceAt: occurrenceAt);
        return DeadLetterOutcome.Settled;
    }

    // The format the fan-out writes its target and its receipts in.
    private static string OccurrenceText(DateTimeOffset occurrenceAt) =>
        occurrenceAt.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

    private static async Task DeleteFailureAsync(NpgsqlTransaction transaction, string kind, string target, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.delivery_failures WHERE kind = @kind AND target = @target",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("target", target);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DeadLetterRow?> LockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid applicationId,
        Guid processId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {DeadLetters.Columns} FROM axis.dead_letters WHERE id = @id AND application_id = @application AND process_id = @process FOR UPDATE",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? DeadLetters.Read(reader) : null;
    }

    private static async Task<DeadLetterRow> UpdateAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string state,
        string reason,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"UPDATE axis.dead_letters SET state = @state, reason = @reason, settled_by = @user, settled_at = now() WHERE id = @id RETURNING {DeadLetters.Columns}",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? DeadLetters.Read(reader)
            : throw new InvalidOperationException($"The locked dead letter '{id}' could not be settled.");
    }
}
