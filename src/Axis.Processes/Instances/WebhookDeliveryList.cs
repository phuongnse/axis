using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Lists the webhook deliveries of the tenant from <c>axis.webhook_deliveries</c> over a connection to a tenant
/// database. A row holds ids, a state, a count and a status code, never a body, a URL or a secret. The list does not
/// read the event occurrence, so no payload value leaves the database.
/// </summary>
public static class WebhookDeliveryList
{
    /// <summary>The receiver answered <c>2xx</c>.</summary>
    public const string Delivered = "delivered";

    /// <summary>The delivery is sent or will be tried again.</summary>
    public const string Pending = "pending";

    /// <summary>The delivery failed too many times and its dead letter waits for an operator.</summary>
    public const string DeadLettered = "deadLettered";

    /// <summary>The delivery failed too many times and an operator closed its dead letter.</summary>
    public const string Discarded = "discarded";

    // The worker leaves a dead-lettered delivery 'pending', so the newest dead letter of the occurrence and the webhook
    // decides. A redelivered dead letter is queued again and shows as pending.
    private const string StateSql =
        """
        CASE
            WHEN d.state = 'delivered' THEN 'delivered'
            WHEN dl.state = 'open' THEN 'deadLettered'
            WHEN dl.state = 'discarded' THEN 'discarded'
            ELSE 'pending'
        END
        """;

    private const string Where =
        "WHERE (@application::uuid IS NULL OR d.application_id = @application) AND (@webhook::uuid IS NULL OR d.webhook_id = @webhook)";

    /// <summary>
    /// Reads one page of the webhook deliveries, newest first, and the number of them on every page.
    /// <paramref name="applicationId"/> and <paramref name="webhookId"/> each keep one value, or every value when
    /// they are <see langword="null"/>.
    /// </summary>
    public static async Task<WebhookDeliveryPage> ListAsync(
        NpgsqlConnection connection,
        Guid? applicationId,
        Guid? webhookId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM axis.webhook_deliveries d {Where}", connection))
        {
            AddFilter(count, applicationId, webhookId);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(
            $"""
            SELECT d.id, d.application_id, d.release_id, d.webhook_id, d.event_id, {StateSql}, d.attempts, d.status_code, d.created_at, d.delivered_at
            FROM axis.webhook_deliveries d
            LEFT JOIN LATERAL (
                SELECT l.state FROM axis.dead_letters l
                WHERE l.kind = @kind AND l.event_id = d.event_id AND l.webhook_id = d.webhook_id
                ORDER BY l.created_at DESC
                LIMIT 1) dl ON true
            {Where}
            ORDER BY d.created_at DESC, d.id DESC
            LIMIT @limit OFFSET @offset
            """,
            connection);
        AddFilter(command, applicationId, webhookId);
        command.Parameters.AddWithValue("kind", DeliveryFailures.WebhookDeliverKind);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<WebhookDeliveryListRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new WebhookDeliveryListRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return new WebhookDeliveryPage(items, totalCount);
    }

    private static void AddFilter(NpgsqlCommand command, Guid? applicationId, Guid? webhookId)
    {
        command.Parameters.Add(new NpgsqlParameter("application", NpgsqlDbType.Uuid) { Value = applicationId is null ? DBNull.Value : applicationId });
        command.Parameters.Add(new NpgsqlParameter("webhook", NpgsqlDbType.Uuid) { Value = webhookId is null ? DBNull.Value : webhookId });
    }
}

/// <summary>One delivery of an event occurrence to a webhook, with its state worked out from its dead letters.</summary>
public sealed record WebhookDeliveryListRow(
    Guid Id,
    Guid ApplicationId,
    Guid ReleaseId,
    Guid WebhookId,
    Guid EventId,
    string State,
    int Attempts,
    int? StatusCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);

/// <summary>One page of webhook deliveries, and the number of them on every page.</summary>
public sealed record WebhookDeliveryPage(IReadOnlyList<WebhookDeliveryListRow> Items, long TotalCount);
