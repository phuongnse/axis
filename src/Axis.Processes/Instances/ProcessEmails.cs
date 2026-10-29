using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Profiles;
using Axis.Processes.Work;
using Axis.Tenancy;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the emails that <c>notify</c> steps and task escalations with the <c>email</c> channel stage over a
/// connection to a tenant database: one <c>pending</c> outbox row for each address. It also builds the page link of
/// an email and the <c>Message-ID</c> of a row.
/// </summary>
internal static class ProcessEmails
{
    /// <summary>The host of a <c>Message-ID</c> when the tenant has no host. It is a reserved name that never resolves.</summary>
    internal const string FallbackMessageIdHost = "axis.invalid";

    /// <summary>Inserts a <c>pending</c> row for each of <paramref name="emails"/>. They are created at the transaction's time.</summary>
    internal static async Task InsertAsync(NpgsqlTransaction transaction, IReadOnlyList<NewEmail> emails, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.email_outbox
                (id, application_id, release_id, notification_id, step, address, locale, subject, body, message_id, "values", state, created_at)
            SELECT email.id, email.application_id, email.release_id, email.notification_id, email.step, email.address,
                   email.locale, email.subject, email.body, email.message_id, email.raw_values::jsonb, 'pending', now()
            FROM unnest(@ids, @applications, @releases, @notifications, @steps, @addresses, @locales, @subjects, @bodies, @messageIds, @values)
                AS email(id, application_id, release_id, notification_id, step, address, locale, subject, body, message_id, raw_values)
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = emails.Select(e => e.Id).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("applications", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = emails.Select(e => e.ApplicationId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("releases", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = emails.Select(e => e.ReleaseId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("notifications", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = emails.Select(e => e.NotificationId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("steps", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = emails.Select(e => e.Step).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("addresses", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = emails.Select(e => e.Address).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("locales", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = emails.Select(e => e.Locale).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("subjects", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = emails.Select(e => e.Subject).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("bodies", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = emails.Select(e => e.Body).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("messageIds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = emails.Select(e => e.MessageId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = emails.Select(e => e.Values).ToArray() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Stages the emails of a notification with the <c>email</c> channel, for a <c>notify</c> step or a task escalation:
    /// one <c>pending</c> outbox row for each address and one queued <c>notification.email</c> item for each row. The
    /// addresses are those of the <c>user</c> recipients' profiles, then those of the <c>email</c> recipients. An address
    /// is used once, ignoring letter case, with the locale of its first recipient. A <c>user</c> recipient with no profile
    /// or no email gets none, and <paramref name="noAddress"/> is called with the user id, never the address. An
    /// <c>email</c> recipient gets the release's first locale. <paramref name="step"/> is the name of the <c>notify</c>
    /// step, or of the task step whose escalation notified.
    /// </summary>
    internal static async Task StageAsync(
        WorkItemContext context,
        ApplicationModel model,
        ITenantCatalog? tenants,
        Guid processInstanceId,
        string step,
        NewNotification notification,
        Action<string> noAddress,
        CancellationToken cancellationToken)
    {
        var userIds = notification.Recipients
            .Where(recipient => recipient.Kind == ProcessNotifications.UserRecipient)
            .Select(recipient => recipient.Recipient)
            .ToArray();
        var contacts = await UserProfiles.FindContactsAsync(context.Connection, context.Transaction, userIds, cancellationToken);
        var targets = new List<(string Address, string? Locale)>();
        foreach (var userId in userIds)
        {
            if (contacts.TryGetValue(userId, out var contact) && !string.IsNullOrWhiteSpace(contact.Email))
            {
                targets.Add((contact.Email, contact.Locale));
            }
            else
            {
                noAddress(userId);
            }
        }

        targets.AddRange(notification.EmailAddresses.Select(address => (address, (string?)null)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        targets = [.. targets.Where(target => seen.Add(target.Address))];
        if (targets.Count == 0)
        {
            return;
        }

        var hosts = tenants?.Find(context.Item.TenantId)?.Hosts;
        var host = hosts is { Count: > 0 } ? hosts[0] : null;
        var link = PageLink(model, host, notification.OpenPage, notification.OpenParameters);
        var values = NotificationTexts.RawValues(notification.Values);
        var emails = new List<NewEmail>(targets.Count);
        foreach (var (address, locale) in targets)
        {
            var texts = NotificationTexts.ChooseTexts(model, locale);
            var body = NotificationTexts.Render(texts, notification.BodyKey, values);
            var id = Guid.CreateVersion7();
            emails.Add(new NewEmail(
                id,
                notification.ApplicationId,
                notification.ReleaseId,
                notification.Id,
                step,
                address,
                texts?.Locale,
                NotificationTexts.Render(texts, notification.SubjectKey, values),
                link is null ? body : $"{body}\n\n{link}",
                MessageId(id, host),
                notification.Values.ToJsonString()));
        }

        await InsertAsync(context.Transaction, emails, cancellationToken);
        foreach (var email in emails)
        {
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.NotificationEmailWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                processInstanceId: processInstanceId,
                emailId: email.Id);
        }
    }

    /// <summary>The <c>Message-ID</c> of the row <paramref name="id"/>: its id at the tenant's host, or at a reserved name when the tenant has none.</summary>
    internal static string MessageId(Guid id, string? host) => $"<{id:D}@{host ?? FallbackMessageIdHost}>";

    /// <summary>
    /// The absolute link to the page a notification opens: <c>https://{host}/{site}/{page}</c>, with the page in lower
    /// case and the parameters as query values. The site is the first one whose navigation lists the page, otherwise the
    /// application's first site. Null when no page is opened, or the tenant has no host, or the application has no site.
    /// </summary>
    internal static string? PageLink(ApplicationModel model, string? host, string? page, JsonObject? parameters)
    {
        if (page is null || host is null)
        {
            return null;
        }

        var site = model.Sites.FirstOrDefault(candidate => candidate.Navigation.Any(item => string.Equals(item.Page.Name, page, StringComparison.Ordinal)))
            ?? (model.Sites.Count > 0 ? model.Sites[0] : null);
        if (site is null)
        {
            return null;
        }

        var link = new StringBuilder("https://").Append(host).Append('/').Append(site.Path).Append('/').Append(page.ToLowerInvariant());
        var separator = '?';
        foreach (var (name, value) in parameters ?? [])
        {
            if (value is null)
            {
                continue;
            }

            link.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(NotificationTexts.RawText(value)));
            separator = '&';
        }

        return link.ToString();
    }
}

/// <summary>An email that a <c>notify</c> step or a task escalation stages for one address.</summary>
/// <param name="Id">The row's id, a version 7 UUID.</param>
/// <param name="NotificationId">The id of the step run's or escalation's notification.</param>
/// <param name="Step">The name of the <c>notify</c> step, or of the task step whose escalation notified.</param>
/// <param name="Locale">The locale of the texts the email is rendered in, or null when the release has no texts.</param>
/// <param name="MessageId">The email's <c>Message-ID</c> header.</param>
/// <param name="Values">The raw placeholder values of the step run, as JSON text.</param>
internal sealed record NewEmail(
    Guid Id,
    Guid ApplicationId,
    Guid ReleaseId,
    Guid NotificationId,
    string Step,
    string Address,
    string? Locale,
    string Subject,
    string Body,
    string MessageId,
    string Values);
