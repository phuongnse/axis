using Axis.Identity.Application.Queries.GetWorkspaceSettings;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.PlanLimits;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetWorkspaceSettingsHandlerTests
{
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    [Fact]
    public async Task GetWorkspaceSettings_WhenWorkspaceExists_ReturnsSettingsWithUsage()
    {
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value,
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

        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);
        _planRepo.GetByIdAsync(Workspace.SubscriptionPlanId, Arg.Any<CancellationToken>()).Returns(plan);
        _planLimits.GetUsageSnapshotAsync(WorkspaceId, Arg.Any<CancellationToken>())
            .Returns(new PlanLimitUsageSnapshot(1, 3, 10, 1_000, 2, 3));

        WorkspaceSettingsDto? dto = await new GetWorkspaceSettingsHandler(_workspaceRepo, _planRepo, _planLimits)
            .Handle(new GetWorkspaceSettingsQuery(WorkspaceId), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Acme");
        dto.PlanName.Should().Be("Free");
        dto.Usage.WorkflowsUsed.Should().Be(1);
        dto.Usage.WorkflowsLimit.Should().Be(3);
    }
}
