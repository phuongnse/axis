using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>Seeds catalog subscription plans on startup.</summary>
public sealed class SubscriptionPlanSeeder(IServiceProvider services, ILogger<SubscriptionPlanSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();
        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        int seeded = await EnsureWellKnownPlansAsync(context, cancellationToken);
        if (seeded > 0)
            logger.LogInformation("Seeded {Count} missing subscription plans.", seeded);
    }

    /// <summary>Idempotent seed for well-known catalog plans (startup + integration tests).</summary>
    public static async Task<int> EnsureWellKnownPlansAsync(
        IdentityDbContext context,
        CancellationToken cancellationToken = default)
    {
        SubscriptionPlan[] expectedPlans = CreateWellKnownPlans();
        Guid[] expectedIds = expectedPlans.Select(p => p.Id).ToArray();
        HashSet<Guid> existingIds = await context.SubscriptionPlans
            .Where(p => expectedIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToHashSetAsync(cancellationToken);

        SubscriptionPlan[] missingPlans = expectedPlans.Where(p => !existingIds.Contains(p.Id)).ToArray();
        if (missingPlans.Length == 0)
            return 0;

        await context.SubscriptionPlans.AddRangeAsync(missingPlans, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return missingPlans.Length;
    }

    private static SubscriptionPlan[] CreateWellKnownPlans() =>
    [
        SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId,
            "Free",
            "free",
            monthlyPriceCents: 0,
            maxWorkflows: 3,
            maxExecutionsPerMonth: 1_000,
            maxUsers: 3,
            maxStorageMegabytes: 500,
            isActive: true,
            isAvailableForNewSignups: true),
        SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.ProId,
            "Pro",
            "pro",
            monthlyPriceCents: 4900,
            maxWorkflows: 25,
            maxExecutionsPerMonth: 50_000,
            maxUsers: 25,
            maxStorageMegabytes: 10_240,
            isActive: true,
            isAvailableForNewSignups: true),
        SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.EnterpriseId,
            "Enterprise",
            "enterprise",
            monthlyPriceCents: 0,
            maxWorkflows: null,
            maxExecutionsPerMonth: null,
            maxUsers: null,
            maxStorageMegabytes: null,
            isActive: true,
            isAvailableForNewSignups: false),
    ];

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
