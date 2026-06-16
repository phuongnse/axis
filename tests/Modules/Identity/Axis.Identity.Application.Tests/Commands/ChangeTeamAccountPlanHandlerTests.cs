using Axis.Identity.Application.Commands.ChangeTeamAccountPlan;
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

public class ChangeTeamAccountPlanHandlerTests
{
    private readonly IPlatformAdminAuthorization _platformAdmin = Substitute.For<IPlatformAdminAuthorization>();
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly ITeamAccountPlanChangeLogRepository _changeLogRepo =
        Substitute.For<ITeamAccountPlanChangeLogRepository>();
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ChangeTeamAccountPlanHandler CreateHandler() =>
        new(_platformAdmin, _teamAccountRepo, _planRepo, _changeLogRepo, _planLimitService, _uow);

    [Fact]
    public async Task ChangeTeamAccountPlan_WhenCallerIsNotPlatformAdmin_ReturnsForbidden()
    {
        Guid userId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(userId).Returns(false);

        Result result = await CreateHandler().Handle(
            new ChangeTeamAccountPlanCommand(Guid.NewGuid(), WellKnownSubscriptionPlans.ProId, userId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeTeamAccountPlan_WhenDowngradingOverLimit_AllowsChangeAndRefreshesCache()
    {
        Guid adminId = Guid.NewGuid();
        Guid teamAccountId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(adminId).Returns(true);

        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value!,
            Email.Create("a@acme.com").Value!,
            WellKnownSubscriptionPlans.ProId);
        _teamAccountRepo.GetByIdAsync(teamAccountId).Returns(teamAccount);

        SubscriptionPlan free = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId, "Free", "free", 0, 3, 1_000, 3, 500, true, true);
        _planRepo.GetByIdAsync(WellKnownSubscriptionPlans.FreeId).Returns(free);

        Result result = await CreateHandler().Handle(
            new ChangeTeamAccountPlanCommand(teamAccountId, WellKnownSubscriptionPlans.FreeId, adminId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        teamAccount.SubscriptionPlanId.Should().Be(WellKnownSubscriptionPlans.FreeId);
        await _changeLogRepo.Received(1).AddAsync(
            teamAccountId,
            WellKnownSubscriptionPlans.ProId,
            WellKnownSubscriptionPlans.FreeId,
            adminId,
            Arg.Any<CancellationToken>());
        await _planLimitService.Received(1).RefreshCachedLimitsAsync(teamAccountId, Arg.Any<CancellationToken>());
    }
}
