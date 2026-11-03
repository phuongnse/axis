using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Processes.Work;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Stages, reads and settles the webhook deliveries over a connection to a tenant database, and builds the request
/// body of one. A delivery is one event occurrence sent to one webhook. The row holds no body: each attempt builds the
/// body again from the stored occurrence, so every attempt sends the same bytes.
/// </summary>
internal static class WebhookDeliveries
{
    /// <summary>The state of a delivery that no receiver has answered <c>2xx</c> yet.</summary>
    internal const string Pending = "pending";

    /// <summary>The state of a delivery that a receiver answered <c>2xx</c>.</summary>
    internal const string Delivered = "delivered";

    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    /// <summary>
    /// Inserts a <c>pending</c> row for each webhook of the event in <paramref name="model"/>, in release order, and
    /// queues a <c>webhook.deliver</c> work item for each, in <paramref name="transaction"/>. A transaction that rolls
    /// back stages nothing.
    /// </summary>
    internal static async Task StageAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel model,
        Guid eventModelId,
        Guid occurrenceId,
        Guid applicationId,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
        foreach (var webhook in model.Webhooks.Where(candidate => candidate.Event.Id == eventModelId))
        {
            var id = Guid.CreateVersion7();
            await using (var command = new NpgsqlCommand(
                """
                INSERT INTO axis.webhook_deliveries (id, application_id, release_id, webhook_id, event_id, state, attempts, created_at)
                VALUES (@id, @application, @release, @webhook, @event, 'pending', 0, now())
                """,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("application", applicationId);
                command.Parameters.AddWithValue("release", releaseId);
                command.Parameters.AddWithValue("webhook", webhook.Id);
                command.Parameters.AddWithValue("event", occurrenceId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await WorkItemQueue.EnqueueAsync(
                connection,
                transaction,
                tenantId,
                ProcessStarts.WebhookDeliverWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                webhookDeliveryId: id);
        }
    }

    /// <summary>Returns the delivery <paramref name="id"/> with its occurrence, or <see langword="null"/> when there is none.</summary>
    internal static async Task<StagedDelivery?> LoadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT d.application_id, d.release_id, d.webhook_id, d.state, d.attempts,
                   e.event, e.entity_id, e.subject_id, e.payload::text, e.raised_at, e.process_instance_id, d.event_id, e.accountable_user
            FROM axis.webhook_deliveries d
            JOIN axis.events e ON e.id = d.event_id
            WHERE d.id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StagedDelivery(
            id,
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetInt32(4),
            new StoredEvent(reader.GetGuid(11), reader.GetGuid(0), reader.GetString(5), reader.GetGuid(6), reader.GetGuid(7), JsonNode.Parse(reader.GetString(8)) as JsonObject ?? [], reader.IsDBNull(12) ? null : reader.GetString(12)),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10));
    }

    /// <summary>
    /// The request body of the delivery: <c>type</c>, <c>id</c>, <c>timestamp</c> and <c>data</c>. The timestamp is the
    /// time the occurrence was raised, so the body is the same on every attempt.
    /// </summary>
    internal static string Body(StagedDelivery delivery, EventModel @event) =>
        new JsonObject
        {
            ["type"] = delivery.Event.Event,
            ["id"] = delivery.Event.Id.ToString("D"),
            ["timestamp"] = delivery.RaisedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture),
            ["data"] = new JsonObject
            {
                ["entity"] = @event.Entity.Name,
                ["subjectId"] = delivery.Event.SubjectId.ToString("D"),
                ["fields"] = delivery.Event.Payload.DeepClone(),
            },
        }.ToJsonString();

    /// <summary>Counts an attempt that failed, with the status the receiver answered, or <see langword="null"/> when it answered none.</summary>
    internal static async Task CountAttemptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, int? statusCode, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.webhook_deliveries SET attempts = attempts + 1, status_code = @status WHERE id = @id AND state = 'pending'",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlTypes.NpgsqlDbType.Integer) { Value = statusCode is { } status ? status : DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Marks the delivery <c>delivered</c> after an attempt that the receiver answered with <paramref name="statusCode"/>.</summary>
    internal static async Task MarkDeliveredAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, int statusCode, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.webhook_deliveries SET state = 'delivered', attempts = attempts + 1, status_code = @status, delivered_at = now() WHERE id = @id AND state = 'pending'",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", statusCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>A webhook delivery with the occurrence it sends.</summary>
/// <param name="ReleaseId">The release that holds the webhook, the one the raising instance is pinned to.</param>
/// <param name="WebhookId">The webhook of that release.</param>
/// <param name="Event">The stored occurrence. Its payload is the body's <c>fields</c>.</param>
/// <param name="RaisedAt">When the occurrence was raised.</param>
/// <param name="ProcessInstanceId">The instance whose step raised the occurrence, or null when an endpoint without a process raised it.</param>
internal sealed record StagedDelivery(
    Guid Id,
    Guid ApplicationId,
    Guid ReleaseId,
    Guid WebhookId,
    string State,
    int Attempts,
    StoredEvent Event,
    DateTimeOffset RaisedAt,
    Guid? ProcessInstanceId);
