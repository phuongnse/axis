namespace Axis.Identity.Domain.Subscriptions;

/// <summary>Stable IDs for seeded subscription plans (migration + default org assignment).</summary>
public static class WellKnownSubscriptionPlans
{
    public static readonly Guid FreeId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    public static readonly Guid ProId = Guid.Parse("11111111-1111-1111-1111-111111111102");
    public static readonly Guid EnterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111103");
}
