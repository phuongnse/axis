using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Runs the recurring <c>advanceSubscriptions</c> job: the clock of the subscriptions, see <c>docs/reference/subscriptions.md#one-clock</c>.
/// It makes one move for each subscription whose next date has passed, oldest first, and never two for one subscription in a run.
/// A <c>trialing</c> subscription becomes <c>readOnly</c> and the cell makes the tenant read-only. A <c>readOnly</c> one becomes
/// <c>deleting</c> after <see cref="ReadOnlyWait"/> and the cell is asked to delete the tenant, which starts its own grace window. A
/// <c>deleting</c> one asks the cell about the tenant each <see cref="DeletingCheck"/>: a tenant the cell no longer knows makes it
/// <c>deleted</c>, and a tenant whose deletion an operator cancelled is resumed, made read-only again, and the subscription goes back to
/// <c>readOnly</c>. A <c>deleting</c> one whose tenant has no directory row is <c>deleted</c> too, as the directory correction drops the
/// row once the cell purged the tenant. The waits count from the run that made the move, so a late run delays the next step and never shortens it. A move
/// commits the new state, the next date, a new <see cref="SubscriptionRow.StepId"/> and one audit record in one transaction, after
/// the cell's job succeeded. The cell calls of a move carry the step id as their idempotency key, so a retry gets the cell's first job.
/// A failed cell call, a job that does not end in time or a tenant with no directory row leaves the subscription as it is and logs a
/// warning that names the subscription and the cell, never the tenant. The run goes on with the other subscriptions and throws at
/// the end, so the job queue retries it. After its moves the run queues the timeline emails, see <c>SubscriptionClock.Emails.cs</c>.
/// A subscription whose <see cref="SubscriptionRow.PeriodEndsAt"/> is set is a paid tenant, and its moves are these. An <c>active</c> one is
/// due when its period has ended or an open invoice is <see cref="PastDueAfter"/> past its due date, and it is never due by its next
/// date. An overdue invoice makes it <c>pastDue</c> with no cell call. Otherwise the end of the period moves the period forward once,
/// with no audit record and no cell call, see <see cref="AdvancePeriodAsync"/>. A <c>pastDue</c> one becomes <c>readOnly</c> after
/// <see cref="PastDueGrace"/> and a <c>cancelled</c> one at its next date, which is the end of its period, each with one read-only cell
/// call. A paid tenant stays <c>readOnly</c> for <see cref="PaidReadOnlyWait"/> before it is <c>deleting</c>.
/// </summary>
internal sealed partial class SubscriptionClock(
    NpgsqlDataSource controlPlane,
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    CellClients cells,
    EmailTemplates templates,
    ControlPlaneOptions options,
    TimeProvider clock,
    ILogger<SubscriptionClock> logger) : IJobHandler
{
    public const string JobKind = "advanceSubscriptions";

    /// <summary>How long a trial lasts from its confirm.</summary>
    public static readonly TimeSpan TrialLength = TimeSpan.FromDays(30);

    /// <summary>How long a tenant stays read-only before its deletion is scheduled.</summary>
    public static readonly TimeSpan ReadOnlyWait = TimeSpan.FromDays(30);

    /// <summary>How long after its due date an open invoice makes an <c>active</c> subscription <c>pastDue</c>.</summary>
    public static readonly TimeSpan PastDueAfter = TimeSpan.FromDays(1);

    /// <summary>How long a subscription stays <c>pastDue</c> before it becomes <c>readOnly</c>.</summary>
    public static readonly TimeSpan PastDueGrace = TimeSpan.FromDays(14);

    /// <summary>How long a paid tenant stays read-only before its deletion is scheduled.</summary>
    public static readonly TimeSpan PaidReadOnlyWait = TimeSpan.FromDays(90);

    /// <summary>How long a <c>deleting</c> subscription waits before it asks the cell about the tenant again.</summary>
    public static readonly TimeSpan DeletingCheck = TimeSpan.FromHours(1);

    private const string Active = "active";
    private const string PastDue = "pastDue";
    private const string Cancelled = "cancelled";
    private const string ReadOnly = "readOnly";
    private const string Deleting = "deleting";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var now = clock.GetUtcNow();
        var due = new List<DueSubscription>();
        await using (var select = controlPlane.CreateCommand(
            """
            SELECT s.id, s.tenant_id, s.state, s.step_id, s.period, s.period_ends_at,
                   EXISTS (SELECT 1 FROM billing.invoices i WHERE i.subscription_id = s.id AND i.status = 'open' AND i.due_at <= @overdue)
            FROM control_plane.subscriptions s
            WHERE (s.state IN ('trialing', 'pastDue', 'cancelled', 'readOnly', 'deleting') AND s.next_at <= @now)
               OR (s.state = 'active' AND (s.period_ends_at <= @now
                   OR EXISTS (SELECT 1 FROM billing.invoices i WHERE i.subscription_id = s.id AND i.status = 'open' AND i.due_at <= @overdue)))
            ORDER BY s.next_at, s.id
            """))
        {
            select.Parameters.AddWithValue("now", now);
            select.Parameters.AddWithValue("overdue", now - PastDueAfter);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add(new DueSubscription(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetBoolean(6)));
            }
        }

        var failed = 0;
        foreach (var (id, tenantId, state, stepId, period, periodEndsAt, overdue) in due)
        {
            if (state == Active)
            {
                // An active subscription makes no cell call, so it needs no cell. A move to pastDue wins over the period's move.
                if (overdue)
                {
                    await CommitAsync(job, id, Active, stepId, PastDue, now + PastDueGrace, cancellationToken);
                }
                else if (periodEndsAt is { } end)
                {
                    await AdvancePeriodAsync(id, end, period, cancellationToken);
                }

                continue;
            }

            var cellId = await TenantCells.FindCellIdAsync(directory, tenantId, cancellationToken);
            if (cellId is null && state == Deleting)
            {
                // The directory correction drops the row of a tenant that the cell purged, so no row means the tenant is gone.
                await CommitAsync(job, id, Deleting, stepId, "deleted", null, cancellationToken);
                continue;
            }

            var cell = TenantCells.Client(cells, cellId);
            if (cell is null)
            {
                LogNoCell(id);
                failed++;
                continue;
            }

            try
            {
                await MoveAsync(job, cell, id, tenantId, state, stepId, now, cancellationToken);
            }
            catch (Exception exception) when (exception is CellCallException or TimeoutException)
            {
                LogMoveFailed(exception, id, cell.CellId);
                failed++;
            }
        }

        var emailsFailed = await QueueEmailsAsync(now, cancellationToken);
        if (failed > 0 || emailsFailed > 0)
        {
            throw new InvalidOperationException($"{failed} of {due.Count} due subscriptions could not be moved, and {emailsFailed} could not get their emails.");
        }
    }

    private async Task MoveAsync(
        ClaimedJob job, CellClient cell, Guid id, string tenantId, string state, Guid stepId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var path = $"/api/cell/tenants/{Uri.EscapeDataString(tenantId)}";
        switch (state)
        {
            case "trialing":
                await FollowAsync(cell, await cell.PostAsync($"{path}/read-only", new CellOperation("read-only", stepId), null, cancellationToken), cancellationToken);
                await CommitAsync(job, id, "trialing", stepId, ReadOnly, now + ReadOnlyWait, cancellationToken);
                break;
            case PastDue:
            case Cancelled:
                await FollowAsync(cell, await cell.PostAsync($"{path}/read-only", new CellOperation("read-only", stepId), null, cancellationToken), cancellationToken);
                await CommitAsync(job, id, state, stepId, ReadOnly, now + PaidReadOnlyWait, cancellationToken);
                break;
            case ReadOnly:
                await FollowAsync(cell, await cell.DeleteAsync(path, new CellOperation("delete", stepId), cancellationToken), cancellationToken);
                await CommitAsync(job, id, ReadOnly, stepId, Deleting, now + DeletingCheck, cancellationToken);
                break;
            default:
                await CheckDeletionAsync(job, cell, id, tenantId, path, stepId, now, cancellationToken);
                break;
        }
    }

    // The end of a paid period moves the period forward. It is not a move, so it writes no audit record and keeps the step id. It runs
    // once, as the new end is later than now and the old end in the condition no longer matches.
    private async Task AdvancePeriodAsync(Guid id, DateTimeOffset periodEndsAt, string? period, CancellationToken cancellationToken)
    {
        var next = PeriodEnd(periodEndsAt, period);
        await using var advance = controlPlane.CreateCommand(
            "UPDATE control_plane.subscriptions SET period_ends_at = @next, next_at = @next WHERE id = @id AND state = 'active' AND period_ends_at = @old");
        advance.Parameters.AddWithValue("next", next);
        advance.Parameters.AddWithValue("id", id);
        advance.Parameters.AddWithValue("old", periodEndsAt);
        await advance.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The end of the paid period that starts at <paramref name="start"/>: one year for the period <c>year</c> and one month otherwise, on the UTC date.</summary>
    internal static DateTimeOffset PeriodEnd(DateTimeOffset start, string? period)
    {
        var date = start.UtcDateTime;
        return new DateTimeOffset(period == "year" ? date.AddYears(1) : date.AddMonths(1), TimeSpan.Zero);
    }

    private async Task CheckDeletionAsync(
        ClaimedJob job, CellClient cell, Guid id, string tenantId, string path, Guid stepId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var status = await cell.GetTenantStatusAsync(tenantId, cancellationToken);
        if (status is null)
        {
            await CommitAsync(job, id, Deleting, stepId, "deleted", null, cancellationToken);
            return;
        }

        if (status == Deleting)
        {
            // The cell's grace is not over. The check is not a move, so it writes no audit record and keeps the step id.
            await using var wait = controlPlane.CreateCommand(
                "UPDATE control_plane.subscriptions SET next_at = @next WHERE id = @id AND state = 'deleting' AND step_id = @step");
            wait.Parameters.AddWithValue("next", now + DeletingCheck);
            wait.Parameters.AddWithValue("id", id);
            wait.Parameters.AddWithValue("step", stepId);
            await wait.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        // An operator cancelled the deletion, which leaves the tenant suspended, and the cell refuses read-only for a suspended tenant.
        if (status == "suspended")
        {
            await FollowAsync(cell, await cell.PostAsync($"{path}/resume", new CellOperation("resume", stepId), null, cancellationToken), cancellationToken);
        }

        if (status != ReadOnly)
        {
            await FollowAsync(cell, await cell.PostAsync($"{path}/read-only", new CellOperation("read-only", stepId), null, cancellationToken), cancellationToken);
        }

        await CommitAsync(job, id, Deleting, stepId, ReadOnly, now + ReadOnlyWait, cancellationToken);
    }

    internal static async Task FollowAsync(CellClient cell, CellJob queued, CancellationToken cancellationToken)
    {
        var ended = await cell.FollowAsync(queued.Id, cancellationToken);
        if (ended.State != CellJob.Succeeded)
        {
            throw new CellCallException($"The job {ended.Id:D} of the cell '{cell.CellId}' failed.", null);
        }
    }

    // One transaction: a run that lost the race, or a retry after the commit, changes no row and so writes no audit record.
    private async Task CommitAsync(
        ClaimedJob job, Guid id, string from, Guid stepId, string to, DateTimeOffset? nextAt, CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = new NpgsqlCommand(
            "UPDATE control_plane.subscriptions SET state = @to, next_at = @next, step_id = @newStep WHERE id = @id AND state = @from AND step_id = @step",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("to", to);
            update.Parameters.AddWithValue("next", (object?)nextAt ?? DBNull.Value);
            update.Parameters.AddWithValue("newStep", Guid.CreateVersion7());
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("from", from);
            update.Parameters.AddWithValue("step", stepId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return;
            }
        }

        await AuditRecords.AppendAsync(transaction, AuditActorKind.Job, job.Id.ToString(), $"subscription.{to}", id.ToString(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record DueSubscription(Guid Id, string TenantId, string State, Guid StepId, string? Period, DateTimeOffset? PeriodEndsAt, bool Overdue);

    [LoggerMessage(LogLevel.Warning, "The subscription {SubscriptionId} could not be moved: its tenant has no row in the region directory, or its cell is not one of the region's. It stays as it is.")]
    private partial void LogNoCell(Guid subscriptionId);

    [LoggerMessage(LogLevel.Warning, "The subscription {SubscriptionId} could not be moved, as the cell {CellId} did not finish the call. It stays as it is and the next run tries again.")]
    private partial void LogMoveFailed(Exception exception, Guid subscriptionId, string cellId);
}
