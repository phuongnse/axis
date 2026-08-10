using System.Security.Cryptography;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public sealed class WorkspaceTransitionCleanupStoreTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task CleanupStore_WhenTransitionIsTerminal_ClaimsAndMarksItExactlyOnce()
    {
        DateTime now = new(2026, 8, 6, 8, 0, 0, DateTimeKind.Utc);
        WorkspaceContextTransition completed = await PersistTransitionAsync(now, complete: true);
        await PersistTransitionAsync(now, complete: false);
        await using IdentityDbContext context = database.CreateContext();
        WorkspaceTransitionCleanupStore store = new(context);

        IReadOnlyList<WorkspaceTransitionCleanupItem> due =
            await store.ListTerminalWithoutRedisCleanupAsync(
                32,
                TestContext.Current.CancellationToken);

        WorkspaceTransitionCleanupItem item = due.Should().ContainSingle(candidate =>
            candidate.TransitionId == completed.Id).Subject;
        item.Status.Should().Be(WorkspaceContextTransitionStatus.Completed);
        item.SourceCorrelationDigest.Should().Be(completed.SourceCorrelationDigest);
        item.TargetCorrelationDigest.Should().Be(completed.TargetCorrelationDigest);
        item.ExpiresAt.Should().Be(new DateTimeOffset(completed.ExpiresAt));
        DateTimeOffset markedAt = new(now.AddMinutes(2));
        (await store.MarkRedisCleanupCompletedAsync(
            completed.Id,
            markedAt,
            TestContext.Current.CancellationToken)).Should().BeTrue();
        (await store.MarkRedisCleanupCompletedAsync(
            completed.Id,
            markedAt,
            TestContext.Current.CancellationToken)).Should().BeFalse();

        await using IdentityDbContext readContext = database.CreateContext();
        WorkspaceContextTransition readBack = await readContext.WorkspaceContextTransitions
            .SingleAsync(
                transition => transition.Id == completed.Id,
                TestContext.Current.CancellationToken);
        readBack.RedisCleanupCompletedAt.Should().Be(markedAt.UtcDateTime);
        readBack.Revision.Should().Be(completed.Revision + 1);
    }

    private async Task<WorkspaceContextTransition> PersistTransitionAsync(
        DateTime now,
        bool complete)
    {
        await using IdentityDbContext context = database.CreateContext();
        User user = User.Create(
            "Cleanup Test User",
            Email.Create($"cleanup-{Guid.NewGuid():N}@example.com").Value);
        Workspace source = Workspace.CreatePersonal(
            "Source Workspace",
            WorkspaceSlug.Create($"source-{Guid.NewGuid():N}").Value);
        Workspace target = Workspace.CreatePersonal(
            "Target Workspace",
            WorkspaceSlug.Create($"target-{Guid.NewGuid():N}").Value);
        WorkspaceContextTransition transition = WorkspaceContextTransition.Begin(
            user.Id,
            source.Id,
            target.Id,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            now,
            now.AddMinutes(5),
            now.AddHours(17));
        if (complete)
            transition.Complete(transition.Revision, now.AddMinutes(1));

        context.AddRange(user, source, target, transition);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return transition;
    }
}
