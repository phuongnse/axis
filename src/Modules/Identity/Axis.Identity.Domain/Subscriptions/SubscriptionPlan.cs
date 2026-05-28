namespace Axis.Identity.Domain.Subscriptions;

/// <summary>Platform catalog plan with usage limits.</summary>
public sealed class SubscriptionPlan
{
    public Guid Id { get; private init; }
    public string Name { get; private init; } = string.Empty;
    public string Slug { get; private init; } = string.Empty;
    public int MonthlyPriceCents { get; private init; }
    public int? MaxWorkflows { get; private init; }
    public int? MaxExecutionsPerMonth { get; private init; }
    public int? MaxUsers { get; private init; }
    public long? MaxStorageMegabytes { get; private init; }
    public bool IsActive { get; private init; }
    public bool IsAvailableForNewSignups { get; private init; }

    private SubscriptionPlan()
    {
    }

    public static SubscriptionPlan Create(
        Guid id,
        string name,
        string slug,
        int monthlyPriceCents,
        int? maxWorkflows,
        int? maxExecutionsPerMonth,
        int? maxUsers,
        long? maxStorageMegabytes,
        bool isActive,
        bool isAvailableForNewSignups)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Plan id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Plan name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Plan slug is required.", nameof(slug));
        if (monthlyPriceCents < 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyPriceCents), "Monthly price cannot be negative.");
        if (maxWorkflows is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWorkflows), "Max workflows must be greater than zero.");
        if (maxExecutionsPerMonth is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExecutionsPerMonth), "Max executions per month must be greater than zero.");
        if (maxUsers is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxUsers), "Max users must be greater than zero.");
        if (maxStorageMegabytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxStorageMegabytes), "Max storage megabytes must be greater than zero.");

        return new SubscriptionPlan
        {
            Id = id,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            MonthlyPriceCents = monthlyPriceCents,
            MaxWorkflows = maxWorkflows,
            MaxExecutionsPerMonth = maxExecutionsPerMonth,
            MaxUsers = maxUsers,
            MaxStorageMegabytes = maxStorageMegabytes,
            IsActive = isActive,
            IsAvailableForNewSignups = isAvailableForNewSignups,
        };
    }

    public bool HasLimit(int? limit) => limit is > 0;

    public bool IsWithinLimit(int? limit, int current, int increment) =>
        !HasLimit(limit) || current + increment <= limit!.Value;
}
