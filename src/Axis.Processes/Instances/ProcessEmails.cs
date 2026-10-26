using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the emails that <c>notify</c> steps with the <c>email</c> channel stage over a connection to a tenant
/// database: one <c>pending</c> outbox row for each address. It also builds the page link of an email and the
/// <c>Message-ID</c> of a row.
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
                (id, application_id, release_id, notification_id, step, address, locale, subject, body, message_id, state, created_at)
            SELECT email.id, email.application_id, email.release_id, email.notification_id, email.step, email.address,
                   email.locale, email.subject, email.body, email.message_id, 'pending', now()
            FROM unnest(@ids, @applications, @releases, @notifications, @steps, @addresses, @locales, @subjects, @bodies, @messageIds)
                AS email(id, application_id, release_id, notification_id, step, address, locale, subject, body, message_id)
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
        await command.ExecuteNonQueryAsync(cancellationToken);
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

/// <summary>An email that a <c>notify</c> step stages for one address.</summary>
/// <param name="Id">The row's id, a version 7 UUID.</param>
/// <param name="NotificationId">The id of the step run's notification.</param>
/// <param name="Step">The name of the <c>notify</c> step.</param>
/// <param name="Locale">The locale of the texts the email is rendered in, or null when the release has no texts.</param>
/// <param name="MessageId">The email's <c>Message-ID</c> header.</param>
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
    string MessageId);
