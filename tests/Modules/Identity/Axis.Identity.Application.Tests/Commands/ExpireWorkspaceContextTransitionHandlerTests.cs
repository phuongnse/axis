using Axis.Identity.Application.Commands.ExpireWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class ExpireWorkspaceContextTransitionHandlerTests
{
    [Fact]
    public async Task Handle_WhenConfirmationWindowElapsed_CompensatesAsSystem()
    {
        DateTime createdAt = DateTime.UtcNow.AddMinutes(-10);
        WorkspaceContextTransition transition = WorkspaceContextTransitionHandlerTestData.Pending(createdAt);
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        IIdentityAuditOutbox audit = Substitute.For<IIdentityAuditOutbox>();
        IUnitOfWork uow = Substitute.For<IUnitOfWork>();
        WorkspaceContextTransitionHandlerTestData.ConfigureReadBack(transitions, audit, transition);

        var result = await new ExpireWorkspaceContextTransitionHandler(
            transitions,
            audit,
            uow,
            TimeProvider.System).Handle(
                new ExpireWorkspaceContextTransitionCommand(
                    transition.Id,
                    transition.UserId,
                    WorkspaceContextTransitionHandlerTestData.SourceDigest),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Compensated");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
