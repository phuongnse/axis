using System.Collections.Concurrent;
using Axis.Core.Telemetry;
using Axis.Processes.Work;
using Axis.Tenancy;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Sends the email a <c>notification.email</c> work item names, which a <c>notify</c> step staged in
/// <c>axis.email_outbox</c>, as plain text over SMTP. The mail settings are the tenant's own <c>Email</c> block when it
/// has one, otherwise the host's, never a mix of both. With no <c>Smtp:Host</c> nothing is sent: one transaction makes
/// the item due again after one lease, writes no failure, and the first such item of a tenant logs one warning. The
/// same happens when the settings turn <c>UseTls</c> off and the worker does not run in Development, since only
/// Development sends email without TLS. Otherwise a read with no transaction loads the row. An item with no row is deleted and logged, and a row already
/// <c>sent</c> deletes its item with nothing sent. The send itself takes no transaction. It uses the stored subject,
/// body and <c>Message-ID</c>, and gives up after half the item's lease, so it ends before another worker can claim
/// the item. After a send, one transaction deletes the item, which checks the claim, and marks the row <c>sent</c>. A
/// worker that crashes between the send and that transaction sends the email again with the same <c>Message-ID</c>,
/// so the receiver can drop the copy. A failed send is counted in <c>axis.delivery_failures</c> for the email,
/// storing only the exception type, and the item is due again after the backoff of that count. At
/// <see cref="DeliverySettings.Attempts"/> failures one transaction writes an <c>open</c> dead letter that names the
/// application, the process of the sending instance, the instance and the email, and deletes the item. The row stays
/// <c>pending</c>, and nothing more is sent until an operator redelivers the dead letter.
/// </summary>
internal sealed partial class NotificationEmailHandler(
    EmailDefaults defaults,
    ITenantCatalog? tenants,
    DeliverySettings deliverySettings,
    EmailSecuritySettings security,
    ILogger<NotificationEmailHandler> logger) : IDeliveryWorkItemHandler
{
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _warnedPlain = new(StringComparer.Ordinal);

    public string Kind => ProcessStarts.NotificationEmailWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        var settings = tenants?.Find(item.TenantId)?.Email ?? defaults.Settings;
        if (settings?.Host is not { } host)
        {
            if (_warned.TryAdd(item.TenantId, 0))
            {
                LogNoHost(item.TenantId);
            }

            return await RescheduleAsync(item, connection, item.Lease, cancellationToken)
                ? WorkItemOutcome.Rescheduled
                : WorkItemOutcome.LostClaim;
        }

        if (!settings.UseTls && !security.AllowPlainSmtp)
        {
            if (_warnedPlain.TryAdd(item.TenantId, 0))
            {
                LogPlainSmtp(item.TenantId);
            }

            return await RescheduleAsync(item, connection, item.Lease, cancellationToken)
                ? WorkItemOutcome.Rescheduled
                : WorkItemOutcome.LostClaim;
        }

        var email = item.EmailId is { } id ? await LoadAsync(connection, id, cancellationToken) : null;
        if (email is null)
        {
            LogNoEmail(item.Id, item.EmailId);
            return await CompleteAsync(item, connection, markSentId: null, cancellationToken);
        }

        if (email.State == "sent")
        {
            return await CompleteAsync(item, connection, markSentId: null, cancellationToken);
        }

        try
        {
            await SendAsync(settings, host, email, item.Lease, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return await CountFailureAsync(item, connection, email.Id, exception, cancellationToken);
        }

        return await CompleteAsync(item, connection, email.Id, cancellationToken);
    }

    /// <summary>
    /// Sends the email with a client of its own, which gives up after half of <paramref name="lease"/>. The disconnect
    /// that follows a sent email cannot fail the send, since a retry would send the email twice.
    /// </summary>
    private static async Task SendAsync(EmailSettings settings, string host, StagedEmail email, TimeSpan lease, CancellationToken cancellationToken)
    {
        var message = new MimeMessage
        {
            Subject = email.Subject,
            Body = new TextPart("plain") { Text = email.Body },

            // MimeKit adds the angle brackets of the stored Message-ID itself.
            MessageId = email.MessageId.Trim('<', '>'),
        };
        message.From.Add(MailboxAddress.Parse(settings.From ?? throw new InvalidOperationException("The mail settings have no sender.")));
        message.To.Add(MailboxAddress.Parse(email.Address));

        using var client = new SmtpClient { Timeout = (int)(lease.TotalMilliseconds / 2) };
        var security = !settings.UseTls
            ? SecureSocketOptions.None
            : settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(host, settings.Port, security, cancellationToken);
        if (settings.UserName is not null)
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password?.Reveal() ?? "", cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        try
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The server has the email, and the client is disposed next.
        }
    }

    /// <summary>
    /// Makes the item due after <paramref name="delay"/> in a transaction of its own. Returns <see langword="false"/>
    /// when another worker holds the item now, so nothing was written.
    /// </summary>
    private async Task<bool> RescheduleAsync(ClaimedWorkItem item, NpgsqlConnection connection, TimeSpan delay, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, delay, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.EmailId);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Deletes the item and, when <paramref name="markSentId"/> is given, marks that row <c>sent</c> in the same
    /// transaction. Another worker that holds the item now makes it write nothing.
    /// </summary>
    private async Task<WorkItemOutcome> CompleteAsync(ClaimedWorkItem item, NpgsqlConnection connection, Guid? markSentId, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.EmailId);
            return WorkItemOutcome.LostClaim;
        }

        if (markSentId is { } id)
        {
            await using var command = new NpgsqlCommand(
                "UPDATE axis.email_outbox SET state = 'sent', sent_at = now() WHERE id = @id AND state = 'pending'",
                connection,
                transaction);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return WorkItemOutcome.Completed;
    }

    /// <summary>
    /// Counts the failed send in a transaction of its own. Below the limit it makes the item due after the backoff of
    /// the count. At the limit it writes an <c>open</c> dead letter and deletes the item. Only the exception type is
    /// kept, since its message can quote an address.
    /// </summary>
    private async Task<WorkItemOutcome> CountFailureAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Guid emailId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var error = DeliveryFailures.ErrorText(exception);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var attempts = await DeliveryFailures.CountAsync(transaction, DeliveryFailures.NotificationEmailKind, emailId.ToString("D"), error, cancellationToken);
        if (attempts >= deliverySettings.Attempts)
        {
            // The outbox row does not record the process, so it is read through the instance whose step staged the email.
            var instanceId = item.ProcessInstanceId
                ?? throw new InvalidOperationException($"Email work item '{item.Id}' names no process instance.");
            var (applicationId, processId) = await LoadProcessAsync(connection, transaction, instanceId, cancellationToken);
            var deadLetterId = await DeliveryFailures.InsertEmailDeadLetterAsync(
                transaction, applicationId, processId, instanceId, emailId, attempts, error, cancellationToken);
            if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, item.EmailId);
                return WorkItemOutcome.LostClaim;
            }

            await transaction.CommitAsync(cancellationToken);
            AxisMetrics.RecordDeadLetter(item.TenantId, DeliveryFailures.NotificationEmailKind);
            LogDeadLettered(emailId, attempts, deadLetterId, DeliveryFailures.NotificationEmailKind);
            return WorkItemOutcome.Completed;
        }

        var delay = DeliveryFailures.Backoff(attempts);
        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, delay, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.EmailId);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        LogSendFailed(emailId, error, attempts, deliverySettings.Attempts, delay);
        return WorkItemOutcome.Rescheduled;
    }

    private static async Task<(Guid ApplicationId, Guid ProcessId)> LoadProcessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT application_id, process_id FROM axis.process_instances WHERE id = @instance",
            connection,
            transaction);
        command.Parameters.AddWithValue("instance", instanceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetGuid(1))
            : throw new InvalidOperationException($"Process instance '{instanceId}' of the email work item does not exist.");
    }

    private static async Task<StagedEmail?> LoadAsync(NpgsqlConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT address, subject, body, message_id, state FROM axis.email_outbox WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StagedEmail(id, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    private sealed record StagedEmail(Guid Id, string Address, string Subject, string Body, string MessageId, string State);

    [LoggerMessage(LogLevel.Warning, "Tenant {TenantId} has no Email:Smtp:Host, so its emails are not sent and stay queued. This is logged once for the tenant.")]
    private partial void LogNoHost(string tenantId);

    [LoggerMessage(LogLevel.Warning, "Tenant {TenantId} has Email:Smtp:UseTls off, and only Development sends email without TLS, so its emails are not sent and stay queued. This is logged once for the tenant.")]
    private partial void LogPlainSmtp(string tenantId);

    [LoggerMessage(LogLevel.Warning, "Email work item {ItemId} names no stored email {EmailId}, so it was discarded.")]
    private partial void LogNoEmail(Guid itemId, Guid? emailId);

    [LoggerMessage(LogLevel.Warning, "Sending email {EmailId} failed with {Error}, failure {Attempts} of {Limit}. It is due again in {Delay}.")]
    private partial void LogSendFailed(Guid emailId, string error, int attempts, int limit, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Sending email {EmailId} failed {Attempts} times and became dead letter {DeadLetterId} of kind {Kind}.")]
    private partial void LogDeadLettered(Guid emailId, int attempts, Guid deadLetterId, string kind);

    [LoggerMessage(LogLevel.Warning, "Email work item {ItemId} for email {EmailId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? emailId);
}
