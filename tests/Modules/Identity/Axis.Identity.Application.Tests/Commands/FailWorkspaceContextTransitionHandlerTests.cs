using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Commands.FailWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class FailWorkspaceContextTransitionHandlerTests
{
    [Fact]
    public async Task Handle_WhenTargetStagingFails_RecordsFailedTerminalState()
    {
        WorkspaceContextTransition transition = WorkspaceContextTransitionHandlerTestData.Pending();
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        WorkspaceContextTransitionHandlerTestData.ConfigureReadBack(transitions, audit, transition);

        Result<WorkspaceContextTransitionDto> result = await new FailWorkspaceContextTransitionHandler(
            transitions,
            audit,
            uow,
            TimeProvider.System).Handle(
                new FailWorkspaceContextTransitionCommand(
                    transition.Id,
                    transition.UserId,
                    WorkspaceContextTransitionHandlerTestData.SourceDigest,
                    "test-correlation"),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Failed");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
