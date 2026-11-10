using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Runs the recurring <c>syncQuotas</c> job: queues one <c>writeQuotas</c> job for each subscription that pins a plan and is not <c>deleting</c> or
/// <c>deleted</c>, see <c>docs/reference/plans.md#from-a-plan-to-the-quotas</c>. The writes put back a quota that was changed on a cell by hand and
/// drop an override that has ended. The jobs are queued in one transaction, ordered by subscription id, so a retry never queues part of them.
/// The payload is not used.
/// </summary>
internal sealed class QuotaSync(NpgsqlDataSource controlPlane) : IJobHandler
{
    public const string JobKind = "syncQuotas";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var ids = new List<Guid>();
        await using (var select = new NpgsqlCommand(
            "SELECT id FROM control_plane.subscriptions WHERE plan_code IS NOT NULL AND state NOT IN ('deleting', 'deleted') ORDER BY id",
            connection,
            transaction))
        {
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        foreach (var id in ids)
        {
            await SubscriptionQuotas.EnqueueAsync(connection, transaction, id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
