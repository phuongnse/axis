using System.Globalization;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.SignUp;
using MimeKit;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// The email step of <see cref="RunAsync"/>, see <c>docs/reference/subscriptions.md#emails</c>. After the moves, each subscription gets
/// the one timeline email that <see cref="DueEmail"/> says is due, once per subscription and kind. The people are the owner, who is the
/// person who signed up, and the tenant's administrators, read from the cell, each address once and each in their own language. The
/// emails of a subscription and their row in <c>control_plane.subscription_emails</c> commit in one transaction, so two runs that
/// overlap queue them once. A failed administrators read queues nothing for the subscription, logs a warning that names the subscription
/// and the cell, never the tenant, and counts as a failure, so the run throws and the job queue retries it.
/// </summary>
internal sealed partial class SubscriptionClock
{
    internal const string TrialStarted = "trialStarted";
    internal const string TrialEndsIn7Days = "trialEndsIn7Days";
    internal const string TrialEndsTomorrow = "trialEndsTomorrow";
    internal const string TenantReadOnly = "tenantReadOnly";
    internal const string DeletionIn7Days = "deletionIn7Days";
    internal const string TenantDeleted = "tenantDeleted";

    private static readonly TimeSpan _week = TimeSpan.FromDays(7);
    private static readonly TimeSpan _day = TimeSpan.FromDays(1);

    /// <summary>
    /// The timeline email that is due for a subscription now, or null. Each email's window starts on its day and lasts until the next
    /// window starts, so a run that comes late sends the current warning and never one that the next step has passed. Once a move is
    /// due, until it is made, no email is due, and none is due for a <c>deleting</c> subscription.
    /// </summary>
    internal static string? DueEmail(string state, DateTimeOffset trialEndsAt, DateTimeOffset? nextAt, DateTimeOffset now)
    {
        switch (state)
        {
            case "trialing":
                return now >= trialEndsAt ? null
                    : now >= trialEndsAt - _day ? TrialEndsTomorrow
                    : now >= trialEndsAt - _week ? TrialEndsIn7Days
                    : TrialStarted;
            case ReadOnly:
                return nextAt is not { } next || now >= next ? null
                    : now >= next - _week ? DeletionIn7Days
                    : TenantReadOnly;
            case "deleted":
                return TenantDeleted;
            default:
                return null;
        }
    }

    // Returns the number of subscriptions whose emails could not be queued.
    private async Task<int> QueueEmailsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = new List<EmailCandidate>();
        await using (var select = controlPlane.CreateCommand(
            """
            SELECT s.id, s.tenant_id, s.state, s.trial_ends_at, s.next_at, u.email, u.full_name, u.language,
                   ARRAY(SELECT e.kind FROM control_plane.subscription_emails e WHERE e.subscription_id = s.id)
            FROM control_plane.subscriptions s
            JOIN control_plane.sign_ups u ON u.id = s.sign_up_id
            WHERE s.state IN ('trialing', 'readOnly')
               OR (s.state = 'deleted' AND NOT EXISTS (
                   SELECT 1 FROM control_plane.subscription_emails e WHERE e.subscription_id = s.id AND e.kind = 'tenantDeleted'))
            ORDER BY s.created_at, s.id
            """))
        {
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new EmailCandidate(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                    new Recipient(reader.GetString(5), reader.GetString(6), reader.GetString(7)),
                    reader.GetFieldValue<string[]>(8)));
            }
        }

        var failed = 0;
        foreach (var candidate in candidates)
        {
            if (DueEmail(candidate.State, candidate.TrialEndsAt, candidate.NextAt, now) is not { } kind || candidate.Sent.Contains(kind))
            {
                continue;
            }

            var recipients = await FindRecipientsAsync(candidate, kind, cancellationToken);
            if (recipients is null)
            {
                failed++;
            }
            else if (recipients.Count > 0)
            {
                await QueueAsync(candidate, kind, recipients, cancellationToken);
            }
        }

        return failed;
    }

    // The owner first, then each administrator whose address is new. An empty list means nothing to queue yet, null a failed read.
    // The cell has purged a deleted tenant and its administrators, so the deletion's email goes to the owner only.
    private async Task<List<Recipient>?> FindRecipientsAsync(EmailCandidate candidate, string kind, CancellationToken cancellationToken)
    {
        var recipients = new List<Recipient> { candidate.Owner };
        if (kind == TenantDeleted)
        {
            return recipients;
        }

        // A tenant with no directory row, or in a cell that is not one of the region's, is not provisioned yet.
        var cellId = await FindCellIdAsync(candidate.TenantId, cancellationToken);
        var cell = cellId is null ? null : cells.All.FirstOrDefault(client => string.Equals(client.CellId, cellId, StringComparison.Ordinal));
        if (cell is null)
        {
            return [];
        }

        IReadOnlyList<CellAdministrator>? administrators;
        try
        {
            administrators = await cell.ListAdministratorsAsync(candidate.TenantId, cancellationToken);
        }
        catch (Exception exception) when (exception is CellCallException or TimeoutException)
        {
            LogAdministratorsFailed(exception, candidate.Id, cell.CellId);
            return null;
        }

        // The cell does not know the tenant yet, so the next run asks again.
        if (administrators is null)
        {
            return [];
        }

        foreach (var administrator in administrators)
        {
            if (!MailboxAddress.TryParse(administrator.Email, out _)
                || recipients.Any(recipient => string.Equals(recipient.Email, administrator.Email, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var language = administrator.Language is { } chosen && EmailTemplates.Languages.Contains(chosen) ? chosen : candidate.Owner.Language;
            recipients.Add(new Recipient(administrator.Email, string.IsNullOrWhiteSpace(administrator.DisplayName) ? administrator.Email : administrator.DisplayName, language));
        }

        return recipients;
    }

    // One transaction: the row that records the kind and the jobs of its emails. A run that lost the race inserts no row and queues none.
    private async Task QueueAsync(EmailCandidate candidate, string kind, List<Recipient> recipients, CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO control_plane.subscription_emails (subscription_id, kind) VALUES (@id, @kind) ON CONFLICT DO NOTHING",
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("id", candidate.Id);
            insert.Parameters.AddWithValue("kind", kind);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return;
            }
        }

        var nextStep = kind switch
        {
            TenantDeleted => (DateTimeOffset?)null,
            TrialStarted or TrialEndsIn7Days or TrialEndsTomorrow => candidate.TrialEndsAt,
            _ => candidate.NextAt,
        };
        var placeholders = templates.Placeholders(kind);
        foreach (var recipient in recipients)
        {
            var values = new Dictionary<string, string>
            {
                ["name"] = recipient.Name,
                ["tenantAddress"] = SignUpAddress.Of(options, candidate.TenantId),
                ["billingPortalUrl"] = $"{options.PublicUrl}/tenants/{Uri.EscapeDataString(candidate.TenantId)}",
            };
            if (nextStep is { } at)
            {
                values["nextStepAt"] = FormatUtc(at, recipient.Language);
            }

            var needed = values.Where(value => placeholders.Contains(value.Key)).ToDictionary(value => value.Key, value => value.Value);
            await EmailJobs.EnqueueAsync(connection, transaction, templates, kind, recipient.Language, recipient.Email, needed, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // The services run with invariant globalization, so the format is written out for each language. The time stays in, as the date alone
    // is wrong for part of the day in Vietnam.
    private static string FormatUtc(DateTimeOffset at, string language) => language == "vi"
        ? at.UtcDateTime.ToString("HH:mm 'UTC ngày' dd/MM/yyyy", CultureInfo.InvariantCulture)
        : at.UtcDateTime.ToString("d MMMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    [LoggerMessage(LogLevel.Warning, "The emails of the subscription {SubscriptionId} were not queued, as the cell {CellId} did not give the administrators of its tenant. The next run tries again.")]
    private partial void LogAdministratorsFailed(Exception exception, Guid subscriptionId, string cellId);

    private sealed record Recipient(string Email, string Name, string Language);

    private sealed record EmailCandidate(
        Guid Id, string TenantId, string State, DateTimeOffset TrialEndsAt, DateTimeOffset? NextAt, Recipient Owner, string[] Sent);
}
