using Axis.Identity.Application.Commands.ChangeWorkspacePlan;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class ChangeWorkspacePlanHandlerTests
{
    private readonly IPlatformAdminAuthorization _platformAdmin = Substitute.For<IPlatformAdminAuthorization>();
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IWorkspacePlanChangeLogRepository _changeLogRepo =
        Substitute.For<IWorkspacePlanChangeLogRepository>();
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ChangeWorkspacePlanHandler CreateHandler() =>
        new(_platformAdmin, _workspaceRepo, _planRepo, _changeLogRepo, _planLimitService, _uow);

    [Fact]
    public async Task ChangeWorkspacePlan_WhenCallerIsNotPlatformAdmin_ReturnsForbidden()
    {
        Guid userId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(userId).Returns(false);

        Result result = await CreateHandler().Handle(
            new ChangeWorkspacePlanCommand(Guid.NewGuid(), WellKnownSubscriptionPlans.ProId, userId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeWorkspacePlan_WhenDowngradingOverLimit_AllowsChangeAndRefreshesCache()
    {
        Guid adminId = Guid.NewGuid();
        Guid WorkspaceId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(adminId).Returns(true);

        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            Email.Create("a@acme.com").Value!,
            WellKnownSubscriptionPlans.ProId);
        _workspaceRepo.GetByIdAsync(WorkspaceId).Returns(Workspace);

        SubscriptionPlan free = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId, "Free", "free", 0, 3, 1_000, 3, 500, true, true);
        _planRepo.GetByIdAsync(WellKnownSubscriptionPlans.FreeId).Returns(free);

        Result result = await CreateHandler().Handle(
            new ChangeWorkspacePlanCommand(WorkspaceId, WellKnownSubscriptionPlans.FreeId, adminId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Workspace.SubscriptionPlanId.Should().Be(WellKnownSubscriptionPlans.FreeId);
        await _changeLogRepo.Received(1).AddAsync(
            WorkspaceId,
            WellKnownSubscriptionPlans.ProId,
            WellKnownSubscriptionPlans.FreeId,
            adminId,
            Arg.Any<CancellationToken>());
        await _planLimitService.Received(1).RefreshCachedLimitsAsync(WorkspaceId, Arg.Any<CancellationToken>());
    }
}
