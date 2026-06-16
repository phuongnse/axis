using Axis.Identity.Application.Commands.ChangeTenantPlan;
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

public class ChangeTenantPlanHandlerTests
{
    private readonly IPlatformAdminAuthorization _platformAdmin = Substitute.For<IPlatformAdminAuthorization>();
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ISubscriptionPlanRepository _planRepo = Substitute.For<ISubscriptionPlanRepository>();
    private readonly ITenantPlanChangeLogRepository _changeLogRepo =
        Substitute.For<ITenantPlanChangeLogRepository>();
    private readonly IPlanLimitService _planLimitService = Substitute.For<IPlanLimitService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ChangeTenantPlanHandler CreateHandler() =>
        new(_platformAdmin, _tenantRepo, _planRepo, _changeLogRepo, _planLimitService, _uow);

    [Fact]
    public async Task ChangeTenantPlan_WhenCallerIsNotPlatformAdmin_ReturnsForbidden()
    {
        Guid userId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(userId).Returns(false);

        Result result = await CreateHandler().Handle(
            new ChangeTenantPlanCommand(Guid.NewGuid(), WellKnownSubscriptionPlans.ProId, userId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeTenantPlan_WhenDowngradingOverLimit_AllowsChangeAndRefreshesCache()
    {
        Guid adminId = Guid.NewGuid();
        Guid TenantId = Guid.NewGuid();
        _platformAdmin.IsPlatformAdmin(adminId).Returns(true);

        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value!,
            Email.Create("a@acme.com").Value!,
            WellKnownSubscriptionPlans.ProId);
        _tenantRepo.GetByIdAsync(TenantId).Returns(Tenant);

        SubscriptionPlan free = SubscriptionPlan.Create(
            WellKnownSubscriptionPlans.FreeId, "Free", "free", 0, 3, 1_000, 3, 500, true, true);
        _planRepo.GetByIdAsync(WellKnownSubscriptionPlans.FreeId).Returns(free);

        Result result = await CreateHandler().Handle(
            new ChangeTenantPlanCommand(TenantId, WellKnownSubscriptionPlans.FreeId, adminId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Tenant.SubscriptionPlanId.Should().Be(WellKnownSubscriptionPlans.FreeId);
        await _changeLogRepo.Received(1).AddAsync(
            TenantId,
            WellKnownSubscriptionPlans.ProId,
            WellKnownSubscriptionPlans.FreeId,
            adminId,
            Arg.Any<CancellationToken>());
        await _planLimitService.Received(1).RefreshCachedLimitsAsync(TenantId, Arg.Any<CancellationToken>());
    }
}
