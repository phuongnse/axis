using System.Text.Json.Nodes;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.RegionDirectory;
using Axis.Tenancy;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Runs the <c>writeQuotas</c> job: writes the quotas of one subscription's tenant to its cell, see
/// <c>docs/reference/plans.md#from-a-plan-to-the-quotas</c>. The payload is <c>{ subscriptionId }</c>. One <c>PUT /api/cell/tenants/{id}/quotas</c>
/// sets every quota of the pinned plan version, with <c>users</c> as the included users plus the users of each add-on pack the subscription holds,
/// and then each override whose end has not passed. The call sets every key, so a repeat writes the same rows. A subscription that is
/// <c>deleting</c> or <c>deleted</c>, or that pins no plan, gets no call and the job succeeds. An add-on code that the pinned plan version does not
/// list, a tenant with no directory row, a cell that is not one of the region's and a failed cell call fail the job, so the job queue retries it. The
/// job changes no subscription, so it writes no audit record. A failure logs a warning that names the subscription and the cell, never the tenant.
/// </summary>
internal sealed partial class SubscriptionQuotas(
    NpgsqlDataSource controlPlane,
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    CellClients cells,
    PlanCatalogue catalogue,
    TimeProvider clock,
    ILogger<SubscriptionQuotas> logger) : IJobHandler
{
    public const string JobKind = "writeQuotas";

    public string Kind => JobKind;

    /// <summary>
    /// Queues a <c>writeQuotas</c> job for a subscription and returns its id. The start, a plan change, an add-on change and a payment that makes
    /// the subscription <c>active</c> queue it in the <paramref name="transaction"/> of their own change, so both commit or neither does.
    /// </summary>
    public static Task<Guid> EnqueueAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid subscriptionId, CancellationToken cancellationToken) =>
        JobQueue.EnqueueAsync(connection, transaction, JobKind, new JsonObject { ["subscriptionId"] = subscriptionId.ToString() }, null, cancellationToken);

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var id = Guid.TryParse(TenantRecordJobs.Text(job.Payload, "subscriptionId", Kind), out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The {Kind} payload has no valid subscriptionId.");

        await using var select = controlPlane.CreateCommand("SELECT tenant_id, state FROM control_plane.subscriptions WHERE id = $1");
        select.Parameters.AddWithValue(id);
        string tenantId;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.GetString(1) is "deleting" or "deleted")
            {
                return;
            }

            tenantId = reader.GetString(0);
        }

        Dictionary<string, long> set;
        await using (var connection = await controlPlane.OpenConnectionAsync(cancellationToken))
        {
            if (await SubscriptionPlans.ReadAsync(connection, null, id, catalogue, cancellationToken) is not { } pinned)
            {
                return;
            }

            set = await BuildAsync(connection, id, pinned.Plan, cancellationToken);
        }

        var cellId = await TenantCells.FindCellIdAsync(directory, tenantId, cancellationToken);
        var cell = TenantCells.Client(cells, cellId);
        if (cell is null)
        {
            LogNoCell(id);
            throw new InvalidOperationException($"The quotas of the subscription {id} could not be written: its tenant has no row in the region directory, or its cell is not one of the region's.");
        }

        try
        {
            await cell.PutQuotasAsync(tenantId, set, cancellationToken);
        }
        catch (Exception exception) when (exception is CellCallException or TimeoutException)
        {
            LogWriteFailed(exception, id, cell.CellId);
            throw;
        }
    }

    // The plan's quotas, users from the included users and the add-on packs, and then the overrides that have not ended.
    private async Task<Dictionary<string, long>> BuildAsync(NpgsqlConnection connection, Guid id, PlanVersion plan, CancellationToken cancellationToken)
    {
        var set = new Dictionary<string, long>(plan.Quotas, StringComparer.Ordinal);
        var users = plan.IncludedUsers;
        foreach (var (addOn, quantity) in await SubscriptionPlans.ReadAddOnsAsync(connection, null, id, plan, cancellationToken))
        {
            users = checked(users + (addOn.Users * quantity));
        }

        set[QuotaKeys.Users] = users;

        await using var overrides = new NpgsqlCommand("SELECT quota, value FROM control_plane.quota_overrides WHERE subscription_id = $1 AND until > $2", connection);
        overrides.Parameters.AddWithValue(id);
        overrides.Parameters.AddWithValue(clock.GetUtcNow());
        await using var overrideReader = await overrides.ExecuteReaderAsync(cancellationToken);
        while (await overrideReader.ReadAsync(cancellationToken))
        {
            set[overrideReader.GetString(0)] = overrideReader.GetInt64(1);
        }

        return set;
    }

    [LoggerMessage(LogLevel.Warning, "The quotas of the subscription {SubscriptionId} could not be written: its tenant has no row in the region directory, or its cell is not one of the region's. The job tries again.")]
    private partial void LogNoCell(Guid subscriptionId);

    [LoggerMessage(LogLevel.Warning, "The quotas of the subscription {SubscriptionId} could not be written, as the cell {CellId} did not accept the call. The job tries again.")]
    private partial void LogWriteFailed(Exception exception, Guid subscriptionId, string cellId);
}
