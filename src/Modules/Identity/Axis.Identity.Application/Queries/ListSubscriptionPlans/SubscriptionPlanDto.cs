namespace Axis.Identity.Application.Queries.ListSubscriptionPlans;

public sealed record SubscriptionPlanDto(
    Guid Id,
    string Name,
    string Slug,
    int MonthlyPriceCents,
    int? MaxWorkflows,
    int? MaxExecutionsPerMonth,
    int? MaxUsers,
    long? MaxStorageMegabytes,
    IReadOnlyList<string> FeatureFlags,
    bool IsCurrent,
    bool IsAvailableForNewSignups);
