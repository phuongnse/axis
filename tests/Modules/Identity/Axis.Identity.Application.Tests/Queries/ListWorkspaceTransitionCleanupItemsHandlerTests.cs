using Axis.Identity.Application.Queries.ListWorkspaceTransitionCleanupItems;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ListWorkspaceTransitionCleanupItemsHandlerTests
{
    [Fact]
    public async Task Handle_WhenBatchIsNotPositive_RejectsBeforeReadingStore()
    {
        IWorkspaceTransitionCleanupStore store = Substitute.For<IWorkspaceTransitionCleanupStore>();

        Func<Task> act = () => new ListWorkspaceTransitionCleanupItemsHandler(store)
            .Handle(new ListWorkspaceTransitionCleanupItemsQuery(0), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await store.DidNotReceive().ListTerminalWithoutRedisCleanupAsync(
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenStoreReturnsItems_MapsTerminalStatusAndDelegatesBatch()
    {
        Guid completedId = Guid.NewGuid();
        Guid compensatedId = Guid.NewGuid();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        IWorkspaceTransitionCleanupStore store = Substitute.For<IWorkspaceTransitionCleanupStore>();
        store.ListTerminalWithoutRedisCleanupAsync(12, Arg.Any<CancellationToken>()).Returns([
            new WorkspaceTransitionCleanupItem(
                completedId,
                "source-completed",
                "target-completed",
                WorkspaceContextTransitionStatus.Completed,
                expiresAt),
            new WorkspaceTransitionCleanupItem(
                compensatedId,
                "source-compensated",
                "target-compensated",
                WorkspaceContextTransitionStatus.Compensated,
                expiresAt),
        ]);

        IReadOnlyList<WorkspaceTransitionCleanupItemDto> result = await new ListWorkspaceTransitionCleanupItemsHandler(store)
            .Handle(new ListWorkspaceTransitionCleanupItemsQuery(12), TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo([
            new WorkspaceTransitionCleanupItemDto(
                completedId,
                "source-completed",
                "target-completed",
                true,
                expiresAt),
            new WorkspaceTransitionCleanupItemDto(
                compensatedId,
                "source-compensated",
                "target-compensated",
                false,
                expiresAt),
        ]);
        await store.Received(1).ListTerminalWithoutRedisCleanupAsync(12, Arg.Any<CancellationToken>());
    }
}
