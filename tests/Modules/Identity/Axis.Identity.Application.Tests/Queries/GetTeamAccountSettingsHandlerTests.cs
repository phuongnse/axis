using Axis.Identity.Application.Queries.GetTeamAccountSettings;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.PlanLimits;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetTeamAccountSettingsHandlerTests
{
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();

    [Fact]
    public async Task GetTeamAccountSettings_WhenTeamAccountExists_ReturnsSettingsWithUsage()
    {
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value,
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

        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);
        _planRepo.GetByIdAsync(teamAccount.SubscriptionPlanId, Arg.Any<CancellationToken>()).Returns(plan);
        _planLimits.GetUsageSnapshotAsync(TeamAccountId, Arg.Any<CancellationToken>())
            .Returns(new PlanLimitUsageSnapshot(1, 3, 10, 1_000, 2, 3));

        TeamAccountSettingsDto? dto = await new GetTeamAccountSettingsHandler(_teamAccountRepo, _planRepo, _planLimits)
            .Handle(new GetTeamAccountSettingsQuery(TeamAccountId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Acme");
        dto.PlanName.Should().Be("Free");
        dto.Usage.WorkflowsUsed.Should().Be(1);
        dto.Usage.WorkflowsLimit.Should().Be(3);
    }
}
