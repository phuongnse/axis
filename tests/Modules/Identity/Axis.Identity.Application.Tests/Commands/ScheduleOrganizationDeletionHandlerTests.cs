using Axis.Identity.Application.Commands.ScheduleOrganizationDeletion;
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

public class ScheduleOrganizationDeletionHandlerTests
{
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IOrganizationDeletionScheduler _scheduler = Substitute.For<IOrganizationDeletionScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private ScheduleOrganizationDeletionHandler CreateHandler() =>
        new(_orgRepo, _userRepo, _emailSender, _scheduler, _uow);

    [Fact]
    public async Task ScheduleOrganizationDeletion_WhenConfirmationMismatch_ReturnsFailure()
    {
        Organization org = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);

        Result result = await CreateHandler().Handle(
            new ScheduleOrganizationDeletionCommand(OrgId, UserId, "Wrong"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _scheduler.DidNotReceive().ScheduleHardDeleteAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleOrganizationDeletion_WhenSchedulerFails_RollsBackAndReturnsFailure()
    {
        Organization org = Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("A", "B", Email.Create("admin@acme.com").Value);
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);
        _userRepo.GetByIdAsync(UserId, OrgId, Arg.Any<CancellationToken>()).Returns(user);
        _scheduler.ScheduleHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("queue down"));

        Result result = await CreateHandler().Handle(
            new ScheduleOrganizationDeletionCommand(OrgId, UserId, "Acme"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        org.Status.Should().Be(OrganizationStatus.Active);
        await _emailSender.DidNotReceive().SendOrganizationDeletionScheduledEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
