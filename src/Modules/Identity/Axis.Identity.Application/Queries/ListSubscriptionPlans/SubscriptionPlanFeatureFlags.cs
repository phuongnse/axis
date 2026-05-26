namespace Axis.Identity.Application.Queries.ListSubscriptionPlans;

internal static class SubscriptionPlanFeatureFlags
{
    public static IReadOnlyList<string> ForSlug(string slug) =>
        slug switch
        {
            "free" => ["core_workflows", "forms", "community_support"],
            "pro" => ["core_workflows", "forms", "integrations", "priority_support"],
            "enterprise" => ["core_workflows", "forms", "integrations", "sso", "dedicated_support", "sla"],
            _ => ["core_workflows"],
        };
}
