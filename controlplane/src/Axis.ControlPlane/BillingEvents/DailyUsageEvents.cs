using System.Globalization;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Npgsql;

namespace Axis.ControlPlane.BillingEvents;

/// <summary>
/// Runs the recurring <c>queueDailyUsage</c> job: queues one <c>usage.daily</c> event for each subscription that is not <c>deleted</c>, for the last
/// finished UTC day, see <c>docs/reference/billing-api.md#daily-usage</c>. The job is queued every hour and sends nothing before <see cref="FinalAfter"/>
/// past midnight, as the cell's row of a day is final only after its first hourly sample of the next day, see <c>docs/reference/tenant-quotas.md#usage</c>.
/// The event's timestamp is the end of the day. Each subscription's day is recorded in <c>usage_day_events</c> in the transaction that queues the
/// event, so a second run, a retry or another replica queues nothing again. Records older than <see cref="KeptDays"/> days are deleted on each run.
/// A subscription whose tenant has no row in the directory, or whose cell answers <c>404</c> or has no row for the day, gets no event: there is no usage
/// to report yet, and the next hour asks again. A cell that is not one of the region's, a failed call and a bad answer skip the subscription, and the job
/// fails after the others, so the job queue retries it. Nothing is sent and no cell is called while <c>ControlPlane:BillingEvents:Url</c> is not set.
/// Logs and the error name the subscription and the cell, never the tenant.
/// </summary>
internal sealed partial class DailyUsageEvents(
    NpgsqlDataSource controlPlane,
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    CellClients cells,
    ControlPlaneOptions options,
    TimeProvider clock,
    ILogger<DailyUsageEvents> logger) : IJobHandler
{
    public const string JobKind = "queueDailyUsage";

    /// <summary>How long after midnight UTC a day's row is final: the cell's first hourly sample of the next day has been written.</summary>
    public static readonly TimeSpan FinalAfter = TimeSpan.FromHours(2);

    /// <summary>How many days the record of a sent day is kept. Only yesterday is ever checked.</summary>
    public const int KeptDays = 30;

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!options.BillingEvents.IsSet)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var day = today.AddDays(-1);
        var end = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (now < end + FinalAfter)
        {
            return;
        }

        await using (var delete = controlPlane.CreateCommand("DELETE FROM control_plane.usage_day_events WHERE day < $1"))
        {
            delete.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Date, day.AddDays(-KeptDays));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var subscriptions = new List<(Guid Id, string TenantId)>();
        await using (var select = controlPlane.CreateCommand(
            """
            SELECT s.id, s.tenant_id FROM control_plane.subscriptions s
            WHERE s.state <> 'deleted' AND s.created_at < $1
              AND NOT EXISTS (SELECT 1 FROM control_plane.usage_day_events e WHERE e.subscription_id = s.id AND e.day = $2)
            ORDER BY s.id
            """))
        {
            select.Parameters.AddWithValue(end);
            select.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Date, day);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                subscriptions.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        var failed = 0;
        foreach (var (id, tenantId) in subscriptions)
        {
            var cellId = await TenantCells.FindCellIdAsync(directory, tenantId, cancellationToken);
            if (cellId is null)
            {
                continue;
            }

            var cell = TenantCells.Client(cells, cellId);
            if (cell is null)
            {
                LogNoCell(id, cellId);
                failed++;
                continue;
            }

            CellUsageDay? usage;
            try
            {
                usage = await cell.GetUsageDayAsync(tenantId, day, cancellationToken);
            }
            catch (Exception exception) when (exception is CellCallException or TimeoutException)
            {
                LogReadFailed(exception, id, cell.CellId);
                failed++;
                continue;
            }

            if (usage is null)
            {
                continue;
            }

            await QueueAsync(id, tenantId, usage, end, cancellationToken);
        }

        if (failed > 0)
        {
            throw new InvalidOperationException($"The daily usage of {failed.ToString(CultureInfo.InvariantCulture)} subscriptions could not be read from their cells.");
        }
    }

    private async Task QueueAsync(Guid subscriptionId, string tenantId, CellUsageDay usage, DateTimeOffset end, CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO control_plane.usage_day_events (subscription_id, day) VALUES ($1, $2) ON CONFLICT DO NOTHING",
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue(subscriptionId);
            insert.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Date, usage.Day);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                // The counts stay null when the cell's row holds none: a zero would be a count that nobody made.
                var data = new JsonObject
                {
                    ["tenantId"] = tenantId,
                    ["day"] = usage.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["databaseBytes"] = usage.DatabaseBytes,
                    ["fileBytes"] = usage.FileBytes,
                    ["users"] = usage.Users,
                    ["applications"] = usage.Applications,
                    ["emailsSent"] = usage.EmailsSent,
                    ["connectorCalls"] = usage.ConnectorCalls,
                    ["workItemsRun"] = usage.WorkItemsRun,
                };
                await BillingEventQueue.EnqueueAsync(transaction, options, BillingEventQueue.UsageDaily, end, data, null, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Warning, "The daily usage of the subscription {SubscriptionId} could not be read: its cell {CellId} is not one of the region's. The job tries again.")]
    private partial void LogNoCell(Guid subscriptionId, string cellId);

    [LoggerMessage(LogLevel.Warning, "The daily usage of the subscription {SubscriptionId} could not be read from the cell {CellId}. The job tries again.")]
    private partial void LogReadFailed(Exception exception, Guid subscriptionId, string cellId);
}
