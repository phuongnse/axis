using Axis.Identity.Application.Commands.MarkWorkspaceTransitionRedisCleanupCompleted;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public sealed class MarkWorkspaceTransitionRedisCleanupCompletedHandlerTests
{
    [Fact]
    public async Task Handle_WhenStoreMarksTransition_ReturnsStoreResult()
    {
        Guid transitionId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IWorkspaceTransitionCleanupStore store = Substitute.For<IWorkspaceTransitionCleanupStore>();
        store.MarkRedisCleanupCompletedAsync(transitionId, now, Arg.Any<CancellationToken>()).Returns(false);

        Result<bool> result = await new MarkWorkspaceTransitionRedisCleanupCompletedHandler(store)
            .Handle(
                new MarkWorkspaceTransitionRedisCleanupCompletedCommand(transitionId, now),
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        await store.Received(1).MarkRedisCleanupCompletedAsync(
            transitionId,
            now,
            Arg.Any<CancellationToken>());
    }
}
