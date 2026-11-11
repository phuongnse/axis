using System.Text.Json.Nodes;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Runs the <c>resumeTenant</c> job: resumes the tenant of a subscription that a payment made <c>active</c> after <c>pastDue</c> or <c>readOnly</c>, see
/// <c>docs/reference/subscriptions.md#states</c>. The payload is <c>{ subscriptionId, stepId }</c>, where the step id is the one that the payment's move
/// gave the subscription. The job calls the cell's resume with the key <c>resume:&lt;step id&gt;</c> and follows the cell's job, so a retry gets the cell's
/// first job. It does nothing when the subscription is gone or its step id has changed, as a later move then owns the tenant and a stale resume must
/// never undo it. A tenant with no directory row, a cell that is not one of the region's and a failed cell call fail the job, so the job queue retries it.
/// The job changes no subscription, so it writes no audit record. A failure logs a warning that names the subscription and the cell, never the tenant.
/// </summary>
internal sealed partial class SubscriptionResume(
    NpgsqlDataSource controlPlane,
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    CellClients cells,
    ILogger<SubscriptionResume> logger) : IJobHandler
{
    public const string JobKind = "resumeTenant";

    public string Kind => JobKind;

    /// <summary>
    /// Queues a <c>resumeTenant</c> job for a subscription and its new <paramref name="stepId"/> and returns its id. The payment that makes the subscription
    /// <c>active</c> queues it in its own <paramref name="transaction"/>, so both commit or neither does.
    /// </summary>
    public static Task<Guid> EnqueueAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid subscriptionId, Guid stepId, CancellationToken cancellationToken) =>
        JobQueue.EnqueueAsync(
            connection,
            transaction,
            JobKind,
            new JsonObject { ["subscriptionId"] = subscriptionId.ToString(), ["stepId"] = stepId.ToString() },
            null,
            cancellationToken);

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var id = Guid.TryParse(TenantRecordJobs.Text(job.Payload, "subscriptionId", Kind), out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The {Kind} payload has no valid subscriptionId.");
        var payloadStep = Guid.TryParse(TenantRecordJobs.Text(job.Payload, "stepId", Kind), out var step)
            ? step
            : throw new InvalidOperationException($"The {Kind} payload has no valid stepId.");

        await using var select = controlPlane.CreateCommand("SELECT tenant_id, step_id FROM control_plane.subscriptions WHERE id = $1");
        select.Parameters.AddWithValue(id);
        string tenantId;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.GetGuid(1) != payloadStep)
            {
                return;
            }

            tenantId = reader.GetString(0);
        }

        var cell = TenantCells.Client(cells, await TenantCells.FindCellIdAsync(directory, tenantId, cancellationToken));
        if (cell is null)
        {
            LogNoCell(id);
            throw new InvalidOperationException($"The tenant of the subscription {id} could not be resumed: its tenant has no row in the region directory, or its cell is not one of the region's.");
        }

        try
        {
            var queued = await cell.PostAsync($"/api/cell/tenants/{Uri.EscapeDataString(tenantId)}/resume", new CellOperation("resume", payloadStep), null, cancellationToken);
            await SubscriptionClock.FollowAsync(cell, queued, cancellationToken);
        }
        catch (Exception exception) when (exception is CellCallException or TimeoutException)
        {
            LogResumeFailed(exception, id, cell.CellId);
            throw;
        }
    }

    [LoggerMessage(LogLevel.Warning, "The tenant of the subscription {SubscriptionId} could not be resumed: its tenant has no row in the region directory, or its cell is not one of the region's. The job tries again.")]
    private partial void LogNoCell(Guid subscriptionId);

    [LoggerMessage(LogLevel.Warning, "The tenant of the subscription {SubscriptionId} could not be resumed, as the cell {CellId} did not finish the call. The job tries again.")]
    private partial void LogResumeFailed(Exception exception, Guid subscriptionId, string cellId);
}
