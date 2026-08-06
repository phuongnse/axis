using Axis.Identity.Application.Queries.ResolveWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Tests.Commands;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ResolveWorkspaceContextTransitionHandlerTests
{
    [Fact]
    public async Task Handle_WhenResolvingTargetCorrelation_ReturnsDurableTransition()
    {
        WorkspaceContextTransition transition = WorkspaceContextTransitionHandlerTestData.Pending();
        IWorkspaceContextTransitionRepository transitions = Substitute.For<IWorkspaceContextTransitionRepository>();
        transitions.GetByTargetCorrelationDigestAsync(
                transition.UserId,
                WorkspaceContextTransitionHandlerTestData.TargetDigest,
                Arg.Any<CancellationToken>())
            .Returns(transition);

        var result = await new ResolveWorkspaceContextTransitionHandler(transitions).Handle(
            new ResolveWorkspaceContextTransitionQuery(
                transition.UserId,
                WorkspaceContextTransitionHandlerTestData.TargetDigest,
                WorkspaceContextTransitionCorrelationRole.Target),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransitionId.Should().Be(transition.Id);
        result.Value.Status.Should().Be("Pending");
    }
}
