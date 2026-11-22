using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Plans;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Sets the plan version and the user packs of a subscription, see <c>docs/reference/billing-api.md#setting-the-plan-and-add-ons</c>. The subscription
/// pins the plan version, its packs are replaced by the ones given, and the move writes <c>subscription.planSet</c> and queues <c>writeQuotas</c> through
/// <see cref="SubscriptionQuotas.EnqueueAsync"/>, as <c>docs/reference/plans.md#from-a-plan-to-the-quotas</c> says. The state and the period stay as they are. The caller
/// has locked the subscription's row, and has checked that the plan version and each add-on are in the catalogue.
/// </summary>
internal static class SubscriptionPlanChange
{
    /// <summary>
    /// Pins <paramref name="plan"/> and replaces the packs with <paramref name="addOns"/> in <paramref name="transaction"/>. Returns false when the subscription is
    /// <c>deleting</c> or <c>deleted</c>, and then changes nothing.
    /// </summary>
    public static async Task<bool> SetAsync(
        NpgsqlTransaction transaction,
        Guid subscriptionId,
        PlanVersion plan,
        IReadOnlyList<(PlanAddOn AddOn, int Quantity)> addOns,
        AuditActorKind actorKind,
        string actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(addOns);
        var connection = transaction.Connection!;
        await using (var update = new NpgsqlCommand(
            "UPDATE control_plane.subscriptions SET plan_code = @code, plan_version = @version WHERE id = @id AND state NOT IN ('deleting', 'deleted')",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("code", plan.Code);
            update.Parameters.AddWithValue("version", plan.Version);
            update.Parameters.AddWithValue("id", subscriptionId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        await using (var delete = new NpgsqlCommand("DELETE FROM control_plane.subscription_add_ons WHERE subscription_id = @id", connection, transaction))
        {
            delete.Parameters.AddWithValue("id", subscriptionId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (addOn, quantity) in addOns)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES (@id, @code, @quantity)",
                connection,
                transaction);
            insert.Parameters.AddWithValue("id", subscriptionId);
            insert.Parameters.AddWithValue("code", addOn.Code);
            insert.Parameters.AddWithValue("quantity", quantity);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditRecords.AppendAsync(transaction, actorKind, actor, "subscription.planSet", subscriptionId.ToString(), cancellationToken);
        await SubscriptionQuotas.EnqueueAsync(connection, transaction, subscriptionId, cancellationToken);
        return true;
    }
}
