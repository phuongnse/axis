using Axis.ControlPlane.Plans;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>The plan version that a subscription pinned, with the currency and period it bought. The currency and period are null when none is chosen.</summary>
public sealed record PinnedPlan(PlanVersion Plan, string? Currency, string? Period)
{
    /// <summary>The price of the pinned period in the pinned currency, or null without a currency.</summary>
    public long? Price => Currency is null || Period is null ? null : Plan.Price(Currency, Period);
}

/// <summary>
/// Reads the plan that subscriptions pin. A subscription keeps the version it bought, whatever versions the settings list later, see
/// <c>docs/reference/plans.md#the-catalogue</c>.
/// </summary>
internal static class SubscriptionPlans
{
    /// <summary>
    /// Every plan code and version that any subscription row names, <c>deleted</c> rows included, because a deleted subscription's history still points at its version.
    /// </summary>
    public static async Task<List<(string Code, int Version)>> ReadPinsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT DISTINCT plan_code, plan_version FROM control_plane.subscriptions WHERE plan_code IS NOT NULL ORDER BY 1, 2",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var pins = new List<(string, int)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            pins.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return pins;
    }

    /// <summary>The plan that the subscription pinned, or null when it pinned none. The version must be in the catalogue, which the start checks.</summary>
    public static async Task<PinnedPlan?> ReadAsync(NpgsqlDataSource dataSource, Guid subscriptionId, PlanCatalogue catalogue, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT plan_code, plan_version, currency, period FROM control_plane.subscriptions WHERE id = $1");
        command.Parameters.AddWithValue(subscriptionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
        {
            return null;
        }

        var code = reader.GetString(0);
        var version = reader.GetInt32(1);
        var plan = catalogue.Find(code, version)
            ?? throw new InvalidOperationException($"The setting ControlPlane:Plans has no plan {code} version {version}, which the subscription pins.");
        return new PinnedPlan(plan, reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3));
    }
}
