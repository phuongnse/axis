using Axis.Identity.Application.Queries.GetTenantSettings;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.PlanLimits;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetTenantSettingsHandlerTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetTenantSettings_WhenTenantExists_ReturnsSettingsWithUsage()
    {
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        SubscriptionPlan plan = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId,
            "Free",
            "free",
            0,
            3,
            1_000,
            3,
            500,
            true,
            true);

        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);
        _planRepo.GetByIdAsync(Tenant.SubscriptionPlanId, Arg.Any<CancellationToken>()).Returns(plan);
        _planLimits.GetUsageSnapshotAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new PlanLimitUsageSnapshot(1, 3, 10, 1_000, 2, 3));

        TenantSettingsDto? dto = await new GetTenantSettingsHandler(_tenantRepo, _planRepo, _planLimits)
            .Handle(new GetTenantSettingsQuery(TenantId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Acme");
        dto.PlanName.Should().Be("Free");
        dto.Usage.WorkflowsUsed.Should().Be(1);
        dto.Usage.WorkflowsLimit.Should().Be(3);
    }
}
