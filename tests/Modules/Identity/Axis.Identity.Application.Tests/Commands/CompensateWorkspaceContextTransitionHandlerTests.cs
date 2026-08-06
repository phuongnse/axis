using Axis.Identity.Application.Commands.CompensateWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class CompensateWorkspaceContextTransitionHandlerTests
{
    [Fact]
    public async Task Handle_WhenSourceRecoversPendingTransition_CompensatesAndConfirmsTerminalAudit()
    {
        WorkspaceContextTransition transition = WorkspaceContextTransitionHandlerTestData.Pending();
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        WorkspaceContextTransitionHandlerTestData.ConfigureReadBack(transitions, audit, transition);

        var result = await new CompensateWorkspaceContextTransitionHandler(
            transitions,
            audit,
            uow,
            TimeProvider.System).Handle(
                new CompensateWorkspaceContextTransitionCommand(
                    transition.Id,
                    transition.UserId,
                    WorkspaceContextTransitionHandlerTestData.SourceDigest,
                    "test-correlation"),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Compensated");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
