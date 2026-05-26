using Axis.Identity.Application.Commands.ChangeOrganizationPlan;
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

public class ChangeOrganizationPlanHandlerTests
{
    private readonly IPlatformAdminAuthorization _platformAdmin = Substitute.For<IPlatformAdminAuthorization>();
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly IOrganizationPlanChangeLogRepository _changeLogRepo =
        Substitute.For<IOrganizationPlanChangeLogRepository>();
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ChangeOrganizationPlanHandler CreateHandler() =>
        new(_platformAdmin, _orgRepo, _planRepo, _changeLogRepo, _planLimitService, _uow);

    [Fact]
    public async Task ChangeOrganizationPlan_WhenCallerIsNotPlatformAdmin_ReturnsForbidden()
    {
        Guid userId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(userId).Returns(false);

        Result result = await CreateHandler().Handle(
            new ChangeOrganizationPlanCommand(Guid.NewGuid(), WellKnownSubscriptionPlans.ProId, userId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeOrganizationPlan_WhenDowngradingOverLimit_AllowsChangeAndRefreshesCache()
    {
        Guid adminId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(adminId).Returns(true);

        Organization org = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value!,
            Email.Create("a@acme.com").Value!,
            WellKnownSubscriptionPlans.ProId);
        _orgRepo.GetByIdAsync(orgId).Returns(org);

        SubscriptionPlan free = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId, "Free", "free", 0, 3, 1_000, 3, 500, true, true);
        _planRepo.GetByIdAsync(WellKnownSubscriptionPlans.FreeId).Returns(free);

        Result result = await CreateHandler().Handle(
            new ChangeOrganizationPlanCommand(orgId, WellKnownSubscriptionPlans.FreeId, adminId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        org.SubscriptionPlanId.Should().Be(WellKnownSubscriptionPlans.FreeId);
        await _changeLogRepo.Received(1).AddAsync(
            orgId,
            WellKnownSubscriptionPlans.ProId,
            WellKnownSubscriptionPlans.FreeId,
            adminId,
            Arg.Any<CancellationToken>());
        await _planLimitService.Received(1).RefreshCachedLimitsAsync(orgId, Arg.Any<CancellationToken>());
    }
}
