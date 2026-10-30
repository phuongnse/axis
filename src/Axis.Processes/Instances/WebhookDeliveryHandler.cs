using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Sends the webhook delivery a <c>webhook.deliver</c> work item names, which a <c>raiseEvent</c> step staged in
/// <c>axis.webhook_deliveries</c>, as a signed Standard Webhooks <c>POST</c>. A transaction first loads the row. An item
/// with no row, or with a row already <c>delivered</c>, is deleted and sends nothing. The model comes from the release
/// the raising instance is pinned to, and the transaction extends the item's lease by the webhook's timeout. The
/// request then takes no transaction. It resolves the <c>url</c> and the <c>secret</c> binding, builds the body again
/// from the stored occurrence, and sends it with <c>webhook-id</c>, <c>webhook-timestamp</c> (now, in Unix seconds) and
/// <c>webhook-signature</c>. A redirect is never followed and the request is cancelled after the webhook's timeout.
/// A <c>2xx</c> answer deletes the item, marks the row <c>delivered</c> and writes the audit record
/// <c>webhook.delivered</c> with actor <c>system</c>, in one transaction. Any other answer, a timeout, a network error, a
/// missing binding or an <c>http</c> URL outside Development is counted in <c>axis.delivery_failures</c> for the delivery,
/// and the item is due again after the backoff of that count. At <see cref="DeliverySettings.Attempts"/> failures one
/// transaction writes an <c>open</c> dead letter that names the application, the process of the raising instance, the
/// instance, the occurrence and the webhook, and deletes the item. The row stays <c>pending</c>, and nothing more is
/// sent until an operator redelivers the dead letter. A worker that crashes after a send sends the request again
/// with the same <c>webhook-id</c>. Each transaction checks the claim token. No body, URL, secret value or exception
/// message is stored or logged.
/// </summary>
internal sealed partial class WebhookDeliveryHandler(
    ReleaseModelCache models,
    IDeploymentBindings bindings,
    ConnectorClient client,
    ConnectorCallSettings settings,
    DeliverySettings deliverySettings,
    TimeProvider time,
    ILogger<WebhookDeliveryHandler> logger) : IDeliveryWorkItemHandler
{
    /// <summary>How much longer than the request itself a claim is leased, so the outcome transaction still holds it.</summary>
    private static readonly TimeSpan _leaseMargin = TimeSpan.FromSeconds(30);

    public string Kind => ProcessStarts.WebhookDeliverWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        StagedDelivery delivery;
        WebhookModel webhook;
        EventModel @event;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var staged = item.WebhookDeliveryId is { } id ? await WebhookDeliveries.LoadAsync(connection, transaction, id, cancellationToken) : null;
            if (staged is null || staged.State == WebhookDeliveries.Delivered)
            {
                if (staged is null)
                {
                    LogNoDelivery(item.Id, item.WebhookDeliveryId);
                }

                return await DiscardAsync(item, connection, transaction, cancellationToken);
            }

            var model = await models.GetAsync(new WorkItemContext(item, connection, transaction), staged.ReleaseId, cancellationToken);
            var found = model.Webhooks.FirstOrDefault(candidate => candidate.Id == staged.WebhookId);
            var foundEvent = found is null ? null : model.Events.FirstOrDefault(candidate => candidate.Id == found.Event.Id);
            if (found is null || foundEvent is null)
            {
                // The release that staged the delivery holds the webhook and its event, so this should not happen.
                LogNoWebhook(item.Id, staged.Id, staged.ReleaseId);
                return await DiscardAsync(item, connection, transaction, cancellationToken);
            }

            delivery = staged;
            webhook = found;
            @event = foundEvent;
            if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, webhook.Timeout + _leaseMargin, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, delivery.Id);
                return WorkItemOutcome.LostClaim;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var answer = await SendAsync(item, delivery, webhook, @event, cancellationToken);
        return answer.Error is null
            ? await DeliveredAsync(item, connection, delivery, webhook, answer.Status!.Value, cancellationToken)
            : await CountFailureAsync(item, connection, delivery, webhook, answer, cancellationToken);
    }

    /// <summary>
    /// Sends the request with no transaction open. An error names the binding or the status, never a value, since an
    /// exception message can quote the URL.
    /// </summary>
    private async Task<Answer> SendAsync(ClaimedWorkItem item, StagedDelivery delivery, WebhookModel webhook, EventModel @event, CancellationToken cancellationToken)
    {
        string url;
        try
        {
            url = bindings.Resolve(webhook.UrlBinding).Value;
        }
        catch (InvalidOperationException)
        {
            return new Answer(null, $"The url binding '{webhook.UrlBinding}' is missing or is not a url.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !(settings.AllowHttp && uri.Scheme == Uri.UriSchemeHttp)))
        {
            return new Answer(null, $"The url binding '{webhook.UrlBinding}' is not an https URL. Only Development allows http.");
        }

        var body = WebhookDeliveries.Body(delivery, @event);
        var id = WebhookSignature.Id(delivery.Event.Id, delivery.WebhookId);
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds();
        string signature;
        try
        {
            signature = WebhookSignature.Sign(id, timestamp, body, bindings.ResolveSecret(webhook.SecretBinding).Reveal());
        }
        catch (InvalidOperationException)
        {
            return new Answer(null, $"The secret binding '{webhook.SecretBinding}' is missing, is not a secret or is not a valid signing secret.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(webhook.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.TryAddWithoutValidation("webhook-id", id);
            request.Headers.TryAddWithoutValidation("webhook-timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("webhook-signature", signature);
            using var response = await client.SendAsync(request, timeout.Token);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new Answer(status, null);
            }

            return new Answer(
                status,
                status is >= 300 and < 400 ? $"HTTP {status} (redirects are not followed)" : $"HTTP {status}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Answer(null, "The request timed out.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Answer(null, DeliveryFailures.ErrorText(exception));
        }
    }

    /// <summary>
    /// Deletes the item, marks the row <c>delivered</c> and writes the audit record, in one transaction. Another worker
    /// that holds the item now makes it write nothing.
    /// </summary>
    private async Task<WorkItemOutcome> DeliveredAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        StagedDelivery delivery,
        WebhookModel webhook,
        int status,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, delivery.Id);
            return WorkItemOutcome.LostClaim;
        }

        await WebhookDeliveries.MarkDeliveredAsync(connection, transaction, delivery.Id, status, cancellationToken);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.WebhookDelivered,
                delivery.ApplicationId,
                delivery.Event.EntityId,
                delivery.Event.SubjectId,
                delivery.ProcessInstanceId,
                new JsonObject
                {
                    ["webhookId"] = webhook.Id,
                    ["webhook"] = webhook.Name,
                    ["eventId"] = delivery.Event.Id,
                    ["statusCode"] = status,
                }),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkItemOutcome.Completed;
    }

    /// <summary>
    /// Counts the failed attempt in a transaction of its own. Below the limit it makes the item due after the backoff of
    /// the count. At the limit it writes an <c>open</c> dead letter and deletes the item.
    /// </summary>
    private async Task<WorkItemOutcome> CountFailureAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        StagedDelivery delivery,
        WebhookModel webhook,
        Answer answer,
        CancellationToken cancellationToken)
    {
        var error = answer.Error!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var attempts = await DeliveryFailures.CountAsync(transaction, DeliveryFailures.WebhookDeliverKind, delivery.Id.ToString("D"), error, cancellationToken);
        await WebhookDeliveries.CountAttemptAsync(connection, transaction, delivery.Id, answer.Status, cancellationToken);
        if (attempts >= deliverySettings.Attempts)
        {
            Guid? processId = delivery.ProcessInstanceId is { } instanceId
                ? await LoadProcessAsync(connection, transaction, instanceId, cancellationToken)
                : null;
            var deadLetterId = await DeliveryFailures.InsertWebhookDeadLetterAsync(
                transaction, delivery.ApplicationId, processId, delivery.ProcessInstanceId, delivery.Event.Id, delivery.WebhookId, attempts, error, cancellationToken);
            if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, delivery.Id);
                return WorkItemOutcome.LostClaim;
            }

            await transaction.CommitAsync(cancellationToken);
            AxisMetrics.RecordDeadLetter(item.TenantId, DeliveryFailures.WebhookDeliverKind);
            LogDeadLettered(webhook.Name, delivery.Id, attempts, deadLetterId, DeliveryFailures.WebhookDeliverKind);
            return WorkItemOutcome.Completed;
        }

        var delay = DeliveryFailures.Backoff(attempts);
        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, delay, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, delivery.Id);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        LogSendFailed(webhook.Name, delivery.Id, error, attempts, deliverySettings.Attempts, delay);
        return WorkItemOutcome.Rescheduled;
    }

    /// <summary>Returns the process of the instance whose step raised the occurrence, which governs the dead letter.</summary>
    private static async Task<Guid> LoadProcessAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid instanceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT process_id FROM axis.process_instances WHERE id = @instance", connection, transaction);
        command.Parameters.AddWithValue("instance", instanceId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid processId
            ? processId
            : throw new InvalidOperationException($"Process instance '{instanceId}' of the webhook delivery does not exist.");
    }

    /// <summary>Deletes the item in the open transaction when the claim is still current.</summary>
    private async Task<WorkItemOutcome> DiscardAsync(ClaimedWorkItem item, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.WebhookDeliveryId);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        return WorkItemOutcome.Completed;
    }

    /// <summary>The answer of one attempt: the status when the receiver answered, and the fixed error text unless it answered <c>2xx</c>.</summary>
    private sealed record Answer(int? Status, string? Error);

    [LoggerMessage(LogLevel.Warning, "Webhook work item {ItemId} names no stored delivery {DeliveryId}, so it was discarded.")]
    private partial void LogNoDelivery(Guid itemId, Guid? deliveryId);

    [LoggerMessage(LogLevel.Warning, "Webhook work item {ItemId} for delivery {DeliveryId} names a webhook or event that release {ReleaseId} does not hold, so it was discarded.")]
    private partial void LogNoWebhook(Guid itemId, Guid deliveryId, Guid releaseId);

    [LoggerMessage(LogLevel.Warning, "Sending webhook {Webhook} for delivery {DeliveryId} failed with {Error}, failure {Attempts} of {Limit}. It is due again in {Delay}.")]
    private partial void LogSendFailed(string webhook, Guid deliveryId, string error, int attempts, int limit, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Sending webhook {Webhook} for delivery {DeliveryId} failed {Attempts} times and became dead letter {DeadLetterId} of kind {Kind}.")]
    private partial void LogDeadLettered(string webhook, Guid deliveryId, int attempts, Guid deadLetterId, string kind);

    [LoggerMessage(LogLevel.Warning, "Webhook work item {ItemId} for delivery {DeliveryId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? deliveryId);
}
