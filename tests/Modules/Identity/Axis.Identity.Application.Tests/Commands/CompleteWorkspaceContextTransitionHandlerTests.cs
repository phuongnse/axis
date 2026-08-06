using Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
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

        var result = await new CompleteWorkspaceContextTransitionHandler(
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
}
