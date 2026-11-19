using System.Collections.Concurrent;
using System.Text;
using Axis.Core.Telemetry;
using Axis.Data.Profiles;
using Axis.Data.Storage;
using Axis.Presentation.Texts;
using Axis.Processes.Work;
using Axis.Tenancy;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Sends the email a <c>notification.email</c> work item names, which a <c>notify</c> step or a task escalation staged in
/// <c>axis.email_outbox</c>, as plain text over SMTP. When the row waits for a person's daily digest, the item is that
/// person's one item for the digest, and it sends one email that lists the subject and link of every row that waits for
/// them, oldest first, in platform texts of their profile locale. It goes to the address of the newest row and has the
/// <c>Message-ID</c> of the newest row. The failure count, the dead letter, the quota slot and the hold are those of the
/// email, keyed by the row the item names. After the send one transaction marks all the listed rows <c>sent</c> and, when
/// a row staged meanwhile still waits, queues the person's next item at their next digest hour. The mail settings are the tenant's own <c>Email</c> block when it
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
/// <c>pending</c>, and nothing more is sent until an operator redelivers the dead letter. In a cell the handler takes a
/// slot of the tenant's <c>emailsPerDay</c> quota just before the send and gives it back when the send fails. With no
/// slot left the email is held: the row stays <c>pending</c> and one transaction makes the item due at the next 00:00 UTC.
/// The first email that is held in a UTC day takes the tenant's <c>emailsHeldNotice</c> counter, and the same transaction
/// stores one notice for the tenant's administrators. A failed or lost transaction gives the counter back. When the
/// platform database cannot be reached for the counter, the email is held with no notice and the first such item of a tenant logs one warning.
/// When the platform database cannot be reached the email is not sent, the item is due again after one lease, and the first
/// such item of a tenant logs one warning. Outside a cell nothing is counted or held.
/// </summary>
internal sealed partial class NotificationEmailHandler(
    EmailDefaults defaults,
    ITenantCatalog? tenants,
    DeliverySettings deliverySettings,
    EmailSecuritySettings security,
    ITextResourceProvider platformTexts,
    ILogger<NotificationEmailHandler> logger,
    IEmailQuota? quota = null) : IDeliveryWorkItemHandler
{
    /// <summary>The locale of the digest texts when the person's profile locale has none.</summary>
    private const string FallbackLocale = "en";

    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _warnedPlain = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _warnedQuota = new(StringComparer.Ordinal);
    private readonly QuotaHold? _hold = quota is null ? null : new(quota, TenantNotices.EmailsHeldSubjectKey, TenantNotices.EmailsHeldBodyKey, logger);

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

        var email = item.EmailId is { } id ? await LoadAsync(item, connection, id, cancellationToken) : null;
        if (email is null)
        {
            LogNoEmail(item.Id, item.EmailId);
            return await CompleteAsync(item, connection, markSent: [], digestUserId: null, cancellationToken);
        }

        if (email.State == "sent")
        {
            return await CompleteAsync(item, connection, markSent: [], digestUserId: null, cancellationToken);
        }

        var outgoing = email.DigestUserId is { } digestUserId
            ? await LoadDigestAsync(item, connection, digestUserId, cancellationToken)
            : new Outgoing(email.Address, email.Subject, email.Body, email.MessageId, [email.Id]);

        EmailSlot? slot = null;
        if (quota is not null)
        {
            try
            {
                slot = await quota.TakeAsync(item.TenantId, cancellationToken);
            }
            catch (NpgsqlException)
            {
                // Sending without counting would break the quota, so the email waits for the platform database.
                if (_warnedQuota.TryAdd(item.TenantId, 0))
                {
                    LogQuotaUnavailable(item.TenantId);
                }

                return await RescheduleAsync(item, connection, item.Lease, cancellationToken)
                    ? WorkItemOutcome.Rescheduled
                    : WorkItemOutcome.LostClaim;
            }

            if (!slot.Value.Taken)
            {
                return await _hold!.HoldAsync(item, connection, slot.Value.Day, slot.Value.Limit ?? 0, cancellationToken);
            }
        }

        try
        {
            await SendAsync(settings, host, outgoing, item.Lease, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (slot is { Taken: true } taken)
            {
                await ReturnSlotAsync(item.TenantId, taken.Day, cancellationToken);
            }

            return await CountFailureAsync(item, connection, email.Id, exception, cancellationToken);
        }

        return await CompleteAsync(item, connection, outgoing.RowIds, email.DigestUserId, cancellationToken);
    }

    /// <summary>
    /// Sends the email with a client of its own, which gives up after half of <paramref name="lease"/>. The disconnect
    /// that follows a sent email cannot fail the send, since a retry would send the email twice.
    /// </summary>
    private static async Task SendAsync(EmailSettings settings, string host, Outgoing email, TimeSpan lease, CancellationToken cancellationToken)
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
        await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
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
    /// Gives back the slot of an email whose send failed. When the platform database cannot be reached the slot stays
    /// taken, which is logged, since the failure of the send is counted either way.
    /// </summary>
    private async Task ReturnSlotAsync(string tenantId, DateOnly day, CancellationToken cancellationToken)
    {
        try
        {
            await quota!.ReturnAsync(tenantId, day, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            LogSlotNotReturned(tenantId, exception.GetType().Name);
        }
    }

    /// <summary>
    /// Deletes the item and marks the rows <paramref name="markSent"/> <c>sent</c> in the same transaction. Another worker
    /// that holds the item now makes it write nothing. For a digest of <paramref name="digestUserId"/> the transaction
    /// first takes the person's digest lock. When a row of theirs that was staged during the send still waits, it queues
    /// the person's next item for that row at their next digest hour.
    /// </summary>
    private async Task<WorkItemOutcome> CompleteAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        IReadOnlyList<Guid> markSent,
        string? digestUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
        if (digestUserId is not null)
        {
            await EmailDigests.LockAsync(transaction, digestUserId, cancellationToken);
        }

        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.EmailId);
            return WorkItemOutcome.LostClaim;
        }

        if (markSent.Count > 0)
        {
            await using var command = new NpgsqlCommand(
                "UPDATE axis.email_outbox SET state = 'sent', sent_at = now() WHERE id = ANY(@ids) AND state = 'pending'",
                connection,
                transaction);
            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = markSent.ToArray() });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (digestUserId is not null)
        {
            await QueueNextDigestAsync(item, connection, transaction, digestUserId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return WorkItemOutcome.Completed;
    }

    private static async Task QueueNextDigestAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string userId,
        CancellationToken cancellationToken)
    {
        Guid nextId;
        Guid? instanceId;
        await using (var command = new NpgsqlCommand(
            "SELECT id, process_instance_id FROM axis.email_outbox WHERE digest_user_id = @user AND state = 'pending' ORDER BY created_at, id LIMIT 1",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("user", userId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            nextId = reader.GetGuid(0);
            instanceId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }

        var choices = await NotificationPreferences.FindEmailChoicesAsync(
            connection, transaction, ProcessNotifications.ProcessSource, [userId], cancellationToken);
        var (hour, zone) = choices.TryGetValue(userId, out var choice)
            ? (choice.DigestHour, choice.TimeZone)
            : (EmailDigests.DefaultHour, EmailDigests.DefaultTimeZone);
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            item.TenantId,
            ProcessStarts.NotificationEmailWorkItemKind,
            EmailDigests.NextAt(DateTimeOffset.UtcNow, hour, zone),
            cancellationToken,
            processInstanceId: instanceId,
            emailId: nextId);
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
        await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
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

    private static async Task<StagedEmail?> LoadAsync(ClaimedWorkItem item, NpgsqlConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT address, subject, body, message_id, state, digest_user_id FROM axis.email_outbox WHERE id = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StagedEmail(
                id, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5))
            : null;
    }

    /// <summary>
    /// Composes the digest of <paramref name="userId"/> from every row that waits for it, oldest first. The texts are the
    /// platform's, in the locale of the person's profile with <c>en</c> as the fallback.
    /// </summary>
    private async Task<Outgoing> LoadDigestAsync(ClaimedWorkItem item, NpgsqlConnection connection, string userId, CancellationToken cancellationToken)
    {
        var rows = new List<(Guid Id, string Address, string Subject, string? Link, string MessageId)>();
        IReadOnlyDictionary<string, UserContact> contacts;
        await using (var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken))
        {
            await using (var command = new NpgsqlCommand(
                "SELECT id, address, subject, link, message_id FROM axis.email_outbox WHERE digest_user_id = @user AND state = 'pending' ORDER BY created_at, id",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("user", userId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
                }
            }

            contacts = await UserProfiles.FindContactsAsync(connection, transaction, [userId], cancellationToken);
        }

        var locale = contacts.TryGetValue(userId, out var contact) ? contact.Locale : null;
        var texts = (locale is null ? null : platformTexts.GetTexts(locale)) ?? platformTexts.GetTexts(FallbackLocale);
        var body = new StringBuilder(Text(texts, "notices.emailDigest.body"));
        foreach (var row in rows)
        {
            body.Append("\n\n").Append(row.Subject);
            if (row.Link is not null)
            {
                body.Append('\n').Append(row.Link);
            }
        }

        var newest = rows[^1];
        return new Outgoing(newest.Address, Text(texts, "notices.emailDigest.subject"), body.ToString(), newest.MessageId, [.. rows.Select(row => row.Id)]);
    }

    private static string Text(TextResources? texts, string key) =>
        texts is not null && texts.Texts.TryGetValue(key, out var text) ? text : key;

    private sealed record StagedEmail(Guid Id, string Address, string Subject, string Body, string MessageId, string State, string? DigestUserId);

    /// <summary>The email the handler sends, and the rows it marks <c>sent</c> afterwards: the one row, or all the rows of a digest.</summary>
    private sealed record Outgoing(string Address, string Subject, string Body, string MessageId, IReadOnlyList<Guid> RowIds);

    [LoggerMessage(LogLevel.Warning, "Tenant {TenantId} has no Email:Smtp:Host, so its emails are not sent and stay queued. This is logged once for the tenant.")]
    private partial void LogNoHost(string tenantId);

    [LoggerMessage(LogLevel.Warning, "Tenant {TenantId} has Email:Smtp:UseTls off, and only Development sends email without TLS, so its emails are not sent and stay queued. This is logged once for the tenant.")]
    private partial void LogPlainSmtp(string tenantId);

    [LoggerMessage(LogLevel.Warning, "The platform database could not be reached to count the emails of tenant {TenantId}, so its emails are not sent and stay queued. This is logged once for the tenant.")]
    private partial void LogQuotaUnavailable(string tenantId);

    [LoggerMessage(LogLevel.Warning, "The slot of a failed email of tenant {TenantId} was not given back, because the platform database failed with {Error}.")]
    private partial void LogSlotNotReturned(string tenantId, string error);

    [LoggerMessage(LogLevel.Warning, "Email work item {ItemId} names no stored email {EmailId}, so it was discarded.")]
    private partial void LogNoEmail(Guid itemId, Guid? emailId);

    [LoggerMessage(LogLevel.Warning, "Sending email {EmailId} failed with {Error}, failure {Attempts} of {Limit}. It is due again in {Delay}.")]
    private partial void LogSendFailed(Guid emailId, string error, int attempts, int limit, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Sending email {EmailId} failed {Attempts} times and became dead letter {DeadLetterId} of kind {Kind}.")]
    private partial void LogDeadLettered(Guid emailId, int attempts, Guid deadLetterId, string kind);

    [LoggerMessage(LogLevel.Warning, "Email work item {ItemId} for email {EmailId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? emailId);
}
