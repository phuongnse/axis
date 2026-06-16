using Axis.Identity.Application.Commands.ScheduleTenantDeletion;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class ScheduleTenantDeletionHandlerTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ITenantDeletionScheduler _scheduler = Substitute.For<ITenantDeletionScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private ScheduleTenantDeletionHandler CreateHandler() =>
        new(_tenantRepo, _userRepo, _emailSender, _scheduler, _uow);

    [Fact]
    public async Task ScheduleTenantDeletion_WhenConfirmationMismatch_ReturnsFailure()
    {
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);

        Result result = await CreateHandler().Handle(
            new ScheduleTenantDeletionCommand(TenantId, UserId, "Wrong"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _scheduler.DidNotReceive().ScheduleHardDeleteAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleTenantDeletion_WhenSchedulerFails_RollsBackAndReturnsFailure()
    {
        Tenant Tenant = Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("A", "B", Email.Create("admin@acme.com").Value);
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);
        _userRepo.GetByIdAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(user);
        _scheduler.ScheduleHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("queue down"));

        Result result = await CreateHandler().Handle(
            new ScheduleTenantDeletionCommand(TenantId, UserId, "Acme"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        Tenant.Status.Should().Be(TenantStatus.Active);
        await _emailSender.DidNotReceive().SendTenantDeletionScheduledEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
