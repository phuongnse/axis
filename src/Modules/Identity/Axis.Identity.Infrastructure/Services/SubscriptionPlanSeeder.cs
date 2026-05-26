using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Identity.Infrastructure.Services;

/// <summary>Seeds catalog subscription plans on startup (US-010).</summary>
public sealed class SubscriptionPlanSeeder(IServiceProvider services, ILogger<SubscriptionPlanSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();
        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        if (await context.SubscriptionPlans.AnyAsync(cancellationToken))
            return;

        SubscriptionPlan[] plans =
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

        await context.SubscriptionPlans.AddRangeAsync(plans, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} subscription plans.", plans.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
