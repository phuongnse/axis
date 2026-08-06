using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.MarkWorkspaceTransitionRedisCleanupCompleted;
using Axis.Identity.Application.Queries.ListWorkspaceTransitionCleanupItems;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using MediatR;
using NSubstitute;

namespace Axis.Api.Tests.Identity;

public sealed class WorkspaceTransitionCleanupBatchTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCompletedRecoveryWindowIsOpen_RemovesOnlySourceTicket()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-06T08:00:00Z");
        WorkspaceTransitionCleanupItemDto item = CompletedItem(now.AddMinutes(1));
        ISender sender = CreateSender([item], []);
        TicketCleanup tickets = new();
        WorkspaceTransitionCleanupBatch cleanup = new(tickets, new FixedTimeProvider(now));

        int inspected = await cleanup.ExecuteAsync(sender, 32, TestContext.Current.CancellationToken);

        inspected.Should().Be(1);
        tickets.Removals.Should().Equal((item.SourceCorrelationDigest, false));
        await sender.DidNotReceive().Send(
            Arg.Any<MarkWorkspaceTransitionRedisCleanupCompletedCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompletedRecoveryWindowExpired_RemovesBothTicketsAndMarksCleanup()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-06T08:00:00Z");
        WorkspaceTransitionCleanupItemDto item = CompletedItem(now);
        List<MarkWorkspaceTransitionRedisCleanupCompletedCommand> marked = [];
        ISender sender = CreateSender([item], marked);
        TicketCleanup tickets = new();
        WorkspaceTransitionCleanupBatch cleanup = new(tickets, new FixedTimeProvider(now));

        int inspected = await cleanup.ExecuteAsync(sender, 32, TestContext.Current.CancellationToken);

        inspected.Should().Be(1);
        tickets.Removals.Should().Equal(
            (item.SourceCorrelationDigest, false),
            (item.TargetCorrelationDigest, true));
        marked.Should().ContainSingle().Which.Should().Be(
            new MarkWorkspaceTransitionRedisCleanupCompletedCommand(item.TransitionId, now));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransitionDidNotComplete_PreservesSourceAndRemovesTargetTicket()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-06T08:00:00Z");
        WorkspaceTransitionCleanupItemDto item = CompletedItem(now.AddMinutes(1)) with
        {
            IsCompleted = false,
        };
        List<MarkWorkspaceTransitionRedisCleanupCompletedCommand> marked = [];
        ISender sender = CreateSender([item], marked);
        TicketCleanup tickets = new();
        WorkspaceTransitionCleanupBatch cleanup = new(tickets, new FixedTimeProvider(now));

        await cleanup.ExecuteAsync(sender, 32, TestContext.Current.CancellationToken);

        tickets.Removals.Should().Equal((item.TargetCorrelationDigest, true));
        marked.Should().ContainSingle();
    }

    private static WorkspaceTransitionCleanupItemDto CompletedItem(DateTimeOffset expiresAt) => new(
        Guid.NewGuid(),
        new string('a', 64),
        new string('b', 64),
        true,
        expiresAt);

    private static ISender CreateSender(
        IReadOnlyList<WorkspaceTransitionCleanupItemDto> items,
        List<MarkWorkspaceTransitionRedisCleanupCompletedCommand> marked)
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(
                Arg.Any<ListWorkspaceTransitionCleanupItemsQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(items);
        sender.Send(
                Arg.Any<MarkWorkspaceTransitionRedisCleanupCompletedCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                marked.Add(call.Arg<MarkWorkspaceTransitionRedisCleanupCompletedCommand>());
                return Result.Success(true);
            });
        return sender;
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
