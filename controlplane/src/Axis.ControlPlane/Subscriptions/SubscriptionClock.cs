using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Runs the recurring <c>advanceSubscriptions</c> job: the clock of the subscriptions, see <c>docs/reference/subscriptions.md#one-clock</c>.
/// It makes one move for each subscription whose next date has passed, oldest first, and never two for one subscription in a run.
/// A <c>trialing</c> subscription becomes <c>readOnly</c> and the cell makes the tenant read-only. A <c>readOnly</c> one becomes
/// <c>deleting</c> after <see cref="ReadOnlyWait"/> and the cell is asked to delete the tenant, which starts its own grace window. A
/// <c>deleting</c> one asks the cell about the tenant each <see cref="DeletingCheck"/>: a tenant the cell no longer knows makes it
/// <c>deleted</c>, and a tenant whose deletion an operator cancelled is resumed, made read-only again, and the subscription goes back to
/// <c>readOnly</c>. The waits count from the run that made the move, so a late run delays the next step and never shortens it. A move
/// commits the new state, the next date, a new <see cref="SubscriptionRow.StepId"/> and one audit record in one transaction, after
/// the cell's job succeeded. The cell calls of a move carry the step id as their idempotency key, so a retry gets the cell's first job.
/// A failed cell call, a job that does not end in time or a tenant with no directory row leaves the subscription as it is and logs a
/// warning that names the subscription and the cell, never the tenant. The run goes on with the other subscriptions and throws at
/// the end, so the job queue retries it.
/// </summary>
internal sealed partial class SubscriptionClock(
    NpgsqlDataSource controlPlane,
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    CellClients cells,
    TimeProvider clock,
    ILogger<SubscriptionClock> logger) : IJobHandler
{
    public const string JobKind = "advanceSubscriptions";

    /// <summary>How long a trial lasts from its confirm.</summary>
    public static readonly TimeSpan TrialLength = TimeSpan.FromDays(30);

    /// <summary>How long a tenant stays read-only before its deletion is scheduled.</summary>
    public static readonly TimeSpan ReadOnlyWait = TimeSpan.FromDays(30);

    /// <summary>How long a <c>deleting</c> subscription waits before it asks the cell about the tenant again.</summary>
    public static readonly TimeSpan DeletingCheck = TimeSpan.FromHours(1);

    private const string ReadOnly = "readOnly";
    private const string Deleting = "deleting";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var now = clock.GetUtcNow();
        var due = new List<(Guid Id, string TenantId, string State, Guid StepId)>();
        await using (var select = controlPlane.CreateCommand(
            """
            SELECT id, tenant_id, state, step_id FROM control_plane.subscriptions
            WHERE state IN ('trialing', 'readOnly', 'deleting') AND next_at <= @now
            ORDER BY next_at, id
            """))
        {
            select.Parameters.AddWithValue("now", now);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3)));
            }
        }

        var failed = 0;
        foreach (var (id, tenantId, state, stepId) in due)
        {
            var cell = await FindCellAsync(tenantId, cancellationToken);
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

        if (failed > 0)
        {
            throw new InvalidOperationException($"{failed} of {due.Count} due subscriptions could not be moved.");
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
            case ReadOnly:
                await FollowAsync(cell, await cell.DeleteAsync(path, new CellOperation("delete", stepId), cancellationToken), cancellationToken);
                await CommitAsync(job, id, ReadOnly, stepId, Deleting, now + DeletingCheck, cancellationToken);
                break;
            default:
                await CheckDeletionAsync(job, cell, id, tenantId, path, stepId, now, cancellationToken);
                break;
        }
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

    // The subscription's tenant lives in the cell that the region directory holds. A tenant with no row, or whose cell this region does not list, has none.
    private async Task<CellClient?> FindCellAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var command = directory.CreateCommand("SELECT cell_id FROM directory.tenants WHERE id = @id");
        command.Parameters.AddWithValue("id", tenantId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not string cellId)
        {
            return null;
        }

        return cells.All.FirstOrDefault(client => string.Equals(client.CellId, cellId, StringComparison.Ordinal));
    }

    private static async Task FollowAsync(CellClient cell, CellJob queued, CancellationToken cancellationToken)
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

    [LoggerMessage(LogLevel.Warning, "The subscription {SubscriptionId} could not be moved: its tenant has no row in the region directory, or its cell is not one of the region's. It stays as it is.")]
    private partial void LogNoCell(Guid subscriptionId);

    [LoggerMessage(LogLevel.Warning, "The subscription {SubscriptionId} could not be moved, as the cell {CellId} did not finish the call. It stays as it is and the next run tries again.")]
    private partial void LogMoveFailed(Exception exception, Guid subscriptionId, string cellId);
}
