using Axis.Identity.Application.Queries.ListSubscriptionPlans;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class ListSubscriptionPlansHandlerTests
{
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();

    [Fact]
    public async Task ListSubscriptionPlans_WhenOrgOnRetiredPlan_IncludesCurrentPlanInResults()
    {
        SubscriptionPlan free = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId, "Free", "free", 0, 3, 1_000, 3, 500, true, true);
        SubscriptionPlan enterprise = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.EnterpriseId,
            "Enterprise",
            "enterprise",
            0,
            null,
            null,
            null,
            null,
            true,
            false);

        _planRepo.ListAvailableForNewSignupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { free });

        Organization org = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            Email.Create("a@acme.com").Value!,
            WellKnownSubscriptionPlans.EnterpriseId);
        _orgRepo.GetByIdAsync(org.Id).Returns(org);
        _planRepo.GetByIdAsync(WellKnownSubscriptionPlans.EnterpriseId).Returns(enterprise);

        IReadOnlyList<SubscriptionPlanDto> result = await new ListSubscriptionPlansHandler(_planRepo, _orgRepo)
            .Handle(new ListSubscriptionPlansQuery(org.Id), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Slug == "enterprise" && p.IsCurrent && !p.IsAvailableForNewSignups);
        result.Should().Contain(p => p.Slug == "free" && !p.IsCurrent);
        result.Single(p => p.Slug == "enterprise").FeatureFlags.Should().NotBeEmpty();
    }
}
