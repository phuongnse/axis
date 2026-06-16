using Axis.Identity.Application.Commands.ScheduleTeamAccountDeletion;
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

public class ScheduleTeamAccountDeletionHandlerTests
{
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ITeamAccountDeletionScheduler _scheduler = Substitute.For<ITeamAccountDeletionScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private ScheduleTeamAccountDeletionHandler CreateHandler() =>
        new(_teamAccountRepo, _userRepo, _emailSender, _scheduler, _uow);

    [Fact]
    public async Task ScheduleTeamAccountDeletion_WhenConfirmationMismatch_ReturnsFailure()
    {
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);

        Result result = await CreateHandler().Handle(
            new ScheduleTeamAccountDeletionCommand(TeamAccountId, UserId, "Wrong"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _scheduler.DidNotReceive().ScheduleHardDeleteAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleTeamAccountDeletion_WhenSchedulerFails_RollsBackAndReturnsFailure()
    {
        TeamAccount teamAccount = TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("A", "B", Email.Create("admin@acme.com").Value);
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);
        _userRepo.GetByIdAsync(UserId, TeamAccountId, Arg.Any<CancellationToken>()).Returns(user);
        _scheduler.ScheduleHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("queue down"));

        Result result = await CreateHandler().Handle(
            new ScheduleTeamAccountDeletionCommand(TeamAccountId, UserId, "Acme"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        teamAccount.Status.Should().Be(TeamAccountStatus.Active);
        await _emailSender.DidNotReceive().SendTeamAccountDeletionScheduledEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
