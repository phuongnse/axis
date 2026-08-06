using Axis.Api.Infrastructure;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

public sealed class WorkspaceTransitionCleanupBatchTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCompletedRecoveryWindowIsOpen_RemovesOnlySourceTicket()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-06T08:00:00Z");
        WorkspaceTransitionCleanupItem item = CompletedItem(now.AddMinutes(1));
        CleanupStore store = new(item);
        TicketCleanup tickets = new();
        WorkspaceTransitionCleanupBatch cleanup = new(tickets, new FixedTimeProvider(now));

        int inspected = await cleanup.ExecuteAsync(store, 32, TestContext.Current.CancellationToken);

        inspected.Should().Be(1);
        tickets.Removals.Should().Equal((item.SourceCorrelationDigest, false));
        store.Marked.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompletedRecoveryWindowExpired_RemovesBothTicketsAndMarksCleanup()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-06T08:00:00Z");
        WorkspaceTransitionCleanupItem item = CompletedItem(now);
        CleanupStore store = new(item);
        TicketCleanup tickets = new();
        WorkspaceTransitionCleanupBatch cleanup = new(tickets, new FixedTimeProvider(now));

        int inspected = await cleanup.ExecuteAsync(store, 32, TestContext.Current.CancellationToken);

        inspected.Should().Be(1);
        tickets.Removals.Should().Equal(
            (item.SourceCorrelationDigest, false),
            (item.TargetCorrelationDigest, true));
        store.Marked.Should().ContainSingle().Which.Should().Be((item.TransitionId, now));
    }

    [Theory]
    [InlineData(WorkspaceContextTransitionStatus.Compensated)]
    [InlineData(WorkspaceContextTransitionStatus.Failed)]
    public async Task ExecuteAsync_WhenTransitionDidNotComplete_PreservesSourceAndRemovesTargetTicket(
        WorkspaceContextTransitionStatus status)
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-06T08:00:00Z");
        WorkspaceTransitionCleanupItem item = CompletedItem(now.AddMinutes(1)) with { Status = status };
        CleanupStore store = new(item);
        TicketCleanup tickets = new();
        WorkspaceTransitionCleanupBatch cleanup = new(tickets, new FixedTimeProvider(now));

        await cleanup.ExecuteAsync(store, 32, TestContext.Current.CancellationToken);

        tickets.Removals.Should().Equal((item.TargetCorrelationDigest, true));
        store.Marked.Should().ContainSingle();
    }

    private static WorkspaceTransitionCleanupItem CompletedItem(DateTimeOffset expiresAt) => new(
        Guid.NewGuid(),
        new string('a', 64),
        new string('b', 64),
        WorkspaceContextTransitionStatus.Completed,
        expiresAt);

    private sealed class CleanupStore(params WorkspaceTransitionCleanupItem[] items)
        : IWorkspaceTransitionCleanupStore
    {
        public List<(Guid TransitionId, DateTimeOffset Now)> Marked { get; } = [];

        public Task<IReadOnlyList<WorkspaceTransitionCleanupItem>> ListTerminalWithoutRedisCleanupAsync(
            int batchSize,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceTransitionCleanupItem>>([.. items.Take(batchSize)]);

        public Task<bool> MarkRedisCleanupCompletedAsync(
            Guid transitionId,
            DateTimeOffset now,
            CancellationToken ct = default)
        {
            Marked.Add((transitionId, now));
            return Task.FromResult(true);
        }
    }

    private sealed class TicketCleanup : IWorkspaceTransitionTicketCleanup
    {
        public List<(string Digest, bool Transition)> Removals { get; } = [];

        public Task RemoveByCorrelationDigestAsync(
            string correlationDigest,
            bool transition,
            CancellationToken cancellationToken)
        {
            Removals.Add((correlationDigest, transition));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
