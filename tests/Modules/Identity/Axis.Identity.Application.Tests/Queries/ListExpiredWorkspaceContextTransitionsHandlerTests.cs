using Axis.Identity.Application.Queries.ListExpiredWorkspaceContextTransitions;
using Axis.Identity.Application.Services;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ListExpiredWorkspaceContextTransitionsHandlerTests
{
    [Fact]
    public async Task Handle_WhenBatchIsNotPositive_RejectsBeforeReadingStore()
    {
        IWorkspaceTransitionExpiryStore store = Substitute.For<IWorkspaceTransitionExpiryStore>();

        Func<Task> act = () => new ListExpiredWorkspaceContextTransitionsHandler(store)
            .Handle(
                new ListExpiredWorkspaceContextTransitionsQuery(DateTimeOffset.UtcNow, 0),
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await store.DidNotReceive().ListExpiredPendingAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenStoreReturnsItems_MapsExpiredItemsAndDelegatesBatch()
    {
        Guid transitionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IWorkspaceTransitionExpiryStore store = Substitute.For<IWorkspaceTransitionExpiryStore>();
        store.ListExpiredPendingAsync(now, 8, Arg.Any<CancellationToken>()).Returns([
            new WorkspaceTransitionExpiryItem(transitionId, userId, "source-digest"),
        ]);

        IReadOnlyList<ExpiredWorkspaceContextTransitionDto> result =
            await new ListExpiredWorkspaceContextTransitionsHandler(store).Handle(
                new ListExpiredWorkspaceContextTransitionsQuery(now, 8),
                TestContext.Current.CancellationToken);

        result.Should().Equal(new ExpiredWorkspaceContextTransitionDto(
            transitionId,
            userId,
            "source-digest"));
        await store.Received(1).ListExpiredPendingAsync(now, 8, Arg.Any<CancellationToken>());
    }
}
