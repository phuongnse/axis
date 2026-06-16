using Axis.Identity.Application.Queries.GetOrganizationSettings;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.PlanLimits;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetOrganizationSettingsHandlerTests
{
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task GetOrganizationSettings_WhenOrganizationExists_ReturnsSettingsWithUsage()
    {
        Organization org = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
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

        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);
        _planRepo.GetByIdAsync(org.SubscriptionPlanId, Arg.Any<CancellationToken>()).Returns(plan);
        _planLimits.GetUsageSnapshotAsync(OrgId, Arg.Any<CancellationToken>())
            .Returns(new PlanLimitUsageSnapshot(1, 3, 10, 1_000, 2, 3));

        OrganizationSettingsDto? dto = await new GetOrganizationSettingsHandler(_orgRepo, _planRepo, _planLimits)
            .Handle(new GetOrganizationSettingsQuery(OrgId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Acme");
        dto.PlanName.Should().Be("Free");
        dto.Usage.WorkflowsUsed.Should().Be(1);
        dto.Usage.WorkflowsLimit.Should().Be(3);
    }
}
