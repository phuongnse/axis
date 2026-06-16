using Axis.Identity.Application.Commands.ScheduleWorkspaceDeletion;
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

public class ScheduleWorkspaceDeletionHandlerTests
{
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IWorkspaceDeletionScheduler _scheduler = Substitute.For<IWorkspaceDeletionScheduler>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private ScheduleWorkspaceDeletionHandler CreateHandler() =>
        new(_workspaceRepo, _userRepo, _emailSender, _scheduler, _uow);

    [Fact]
    public async Task ScheduleWorkspaceDeletion_WhenConfirmationMismatch_ReturnsFailure()
    {
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);

        Result result = await CreateHandler().Handle(
            new ScheduleWorkspaceDeletionCommand(WorkspaceId, UserId, "Wrong"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _scheduler.DidNotReceive().ScheduleHardDeleteAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleWorkspaceDeletion_WhenSchedulerFails_RollsBackAndReturnsFailure()
    {
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);
        User user = User.Create("A", "B", Email.Create("admin@acme.com").Value);
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);
        _userRepo.GetByIdAsync(UserId, WorkspaceId, Arg.Any<CancellationToken>()).Returns(user);
        _scheduler.ScheduleHardDeleteAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("queue down"));

        Result result = await CreateHandler().Handle(
            new ScheduleWorkspaceDeletionCommand(WorkspaceId, UserId, "Acme"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        Workspace.Status.Should().Be(WorkspaceStatus.Active);
        await _emailSender.DidNotReceive().SendWorkspaceDeletionScheduledEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
