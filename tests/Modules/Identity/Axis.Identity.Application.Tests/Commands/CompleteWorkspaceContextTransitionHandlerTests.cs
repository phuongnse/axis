using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Commands.CompensateWorkspaceContextTransition;
using Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class CompleteWorkspaceContextTransitionHandlerTests
{
    [Fact]
    public async Task Handle_WhenTargetIsStillEligible_CompletesAndConfirmsTerminalAudit()
    {
        WorkspaceContextTransition transition = WorkspaceContextTransitionHandlerTestData.Pending();
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        WorkspaceContextTransitionHandlerTestData.ConfigureReadBack(transitions, audit, transition);
        memberships.HasActiveWorkspaceAccessAsync(
                transition.TargetWorkspaceId,
                transition.UserId,
                Arg.Any<CancellationToken>())
            .Returns(true);

        Result<WorkspaceContextTransitionDto> result = await new CompleteWorkspaceContextTransitionHandler(
            transitions,
            memberships,
            audit,
            uow,
            TimeProvider.System).Handle(
                new CompleteWorkspaceContextTransitionCommand(
                    transition.Id,
                    transition.UserId,
                    WorkspaceContextTransitionHandlerTestData.TargetDigest,
                    "test-correlation"),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Completed");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRecoveryFollowsCompletedConfirmation_ReturnsCompletedContext()
    {
        WorkspaceContextTransition transition = WorkspaceContextTransitionHandlerTestData.Pending();
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        AuditEventV1? terminalAudit = null;
        transitions.GetByIdAsync(transition.Id, Arg.Any<CancellationToken>()).Returns(transition);
        memberships.HasActiveWorkspaceAccessAsync(
                transition.TargetWorkspaceId,
                transition.UserId,
                Arg.Any<CancellationToken>())
            .Returns(true);
        audit.EnqueueAsync(Arg.Any<AuditEventV1>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                terminalAudit = call.Arg<AuditEventV1>();
                return Task.CompletedTask;
            });
        audit.GetAsync(transition.TerminalAuditEventId, Arg.Any<CancellationToken>())
            .Returns(_ => terminalAudit is null
                ? null
                : new IdentityAuditOutboxEntry(terminalAudit, IdentityAuditOutboxState.Pending));

        Result<WorkspaceContextTransitionDto> confirmed = await new CompleteWorkspaceContextTransitionHandler(
            transitions,
            memberships,
            audit,
            uow,
            TimeProvider.System).Handle(
                new CompleteWorkspaceContextTransitionCommand(
                    transition.Id,
                    transition.UserId,
                    WorkspaceContextTransitionHandlerTestData.TargetDigest,
                    "confirm-correlation"),
                TestContext.Current.CancellationToken);
        Result<WorkspaceContextTransitionDto> recovered = await new CompensateWorkspaceContextTransitionHandler(
            transitions,
            audit,
            uow,
            TimeProvider.System).Handle(
                new CompensateWorkspaceContextTransitionCommand(
                    transition.Id,
                    transition.UserId,
                    WorkspaceContextTransitionHandlerTestData.SourceDigest,
                    "recover-correlation"),
                TestContext.Current.CancellationToken);

        confirmed.IsSuccess.Should().BeTrue();
        recovered.IsSuccess.Should().BeTrue();
        confirmed.Value.Status.Should().Be("Completed");
        recovered.Value.Status.Should().Be("Completed");
        await audit.Received(1).EnqueueAsync(
            Arg.Is<AuditEventV1>(entry => entry.EventId == transition.TerminalAuditEventId
                && entry.Outcome == "completed"),
            Arg.Any<CancellationToken>());
    }
}
