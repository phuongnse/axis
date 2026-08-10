using Axis.Audit.Contracts;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public sealed class IdentityAuditDispatchStoreTests(IdentityDatabaseFixture database)
{
    [Fact]
    public async Task ClaimDueBatchAsync_WhenWorkersRace_OneLeaseOwnsDeliveryAndRetrySchedule()
    {
        Guid eventId = Guid.NewGuid();
        await EnqueueAsync(eventId);
        DateTimeOffset now = DateTimeOffset.UnixEpoch.AddSeconds(1);
        await using IdentityDbContext firstContext = database.CreateContext();
        await using IdentityDbContext secondContext = database.CreateContext();
        IdentityAuditDispatchStore firstStore = new(firstContext);
        IdentityAuditDispatchStore secondStore = new(secondContext);

        IReadOnlyList<IdentityAuditDispatchItem>[] claims = await Task.WhenAll(
            firstStore.ClaimDueBatchAsync(
                now,
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken),
            secondStore.ClaimDueBatchAsync(
                now,
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken));

        IdentityAuditDispatchItem claimed = claims.SelectMany(items => items).Should().ContainSingle().Subject;
        (await firstStore.MarkDeliveredAsync(
            eventId,
            Guid.NewGuid(),
            now,
            TestContext.Current.CancellationToken)).Should().BeFalse();
        IIdentityAuditDispatchStore owner = claims[0].Count == 1 ? firstStore : secondStore;
        DateTimeOffset retryAt = now.AddMinutes(1);
        (await owner.MarkForRetryAsync(
            eventId,
            claimed.LeaseId,
            retryAt,
            "audit.delivery_transient",
            TestContext.Current.CancellationToken)).Should().BeTrue();

        (await owner.ClaimDueBatchAsync(
            retryAt.AddTicks(-10),
            TimeSpan.FromSeconds(30),
            1,
            TestContext.Current.CancellationToken)).Should().BeEmpty();
        IdentityAuditDispatchItem retry = (await owner.ClaimDueBatchAsync(
            retryAt,
            TimeSpan.FromSeconds(30),
            1,
            TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;
        retry.AttemptCount.Should().Be(2);
        (await owner.MarkDeliveredAsync(
            eventId,
            retry.LeaseId,
            retryAt,
            TestContext.Current.CancellationToken)).Should().BeTrue();

        await using IdentityDbContext readContext = database.CreateContext();
        IdentityAuditOutboxEntry? readBack = await new IdentityAuditOutbox(readContext)
            .GetAsync(eventId, TestContext.Current.CancellationToken);
        readBack!.State.Should().Be(IdentityAuditOutboxState.Delivered);
    }

    [Fact]
    public async Task MarkPoisonedAsync_WhenOneEnvelopeIsRejected_DoesNotBlockAnotherLease()
    {
        Guid poisonedEventId = Guid.NewGuid();
        Guid deliveredEventId = Guid.NewGuid();
        await EnqueueAsync(poisonedEventId);
        await EnqueueAsync(deliveredEventId);
        DateTimeOffset now = DateTimeOffset.UnixEpoch.AddSeconds(1);
        await using IdentityDbContext context = database.CreateContext();
        IdentityAuditDispatchStore store = new(context);
        IReadOnlyList<IdentityAuditDispatchItem> claims = await store.ClaimDueBatchAsync(
            now,
            TimeSpan.FromSeconds(30),
            10,
            TestContext.Current.CancellationToken);
        IdentityAuditDispatchItem poisoned = claims.Single(item => item.Event.EventId == poisonedEventId);
        IdentityAuditDispatchItem delivered = claims.Single(item => item.Event.EventId == deliveredEventId);

        (await store.MarkPoisonedAsync(
            poisoned.Event.EventId,
            poisoned.LeaseId,
            "audit.envelope_invalid",
            TestContext.Current.CancellationToken)).Should().BeTrue();
        (await store.MarkDeliveredAsync(
            delivered.Event.EventId,
            delivered.LeaseId,
            now,
            TestContext.Current.CancellationToken)).Should().BeTrue();

        await using IdentityDbContext readContext = database.CreateContext();
        IdentityAuditOutbox outbox = new(readContext);
        (await outbox.GetAsync(poisonedEventId, TestContext.Current.CancellationToken))!
            .State.Should().Be(IdentityAuditOutboxState.Poisoned);
        (await outbox.GetAsync(deliveredEventId, TestContext.Current.CancellationToken))!
            .State.Should().Be(IdentityAuditOutboxState.Delivered);
    }

    private async Task EnqueueAsync(Guid eventId)
    {
        await using IdentityDbContext context = database.CreateContext();
        IdentityAuditOutbox outbox = new(context);
        await outbox.EnqueueAsync(
            new AuditEventV1(
                eventId,
                AuditActorKindV1.System,
                null,
                null,
                Guid.NewGuid(),
                "identity.test",
                "IdentityAudit",
                eventId,
                "succeeded",
                DateTimeOffset.UtcNow,
                Guid.NewGuid().ToString("N")),
            TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await context.IdentityAuditOutboxRecords
            .Where(record => record.EventId == eventId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    record => record.NextAttemptAt,
                    DateTimeOffset.UnixEpoch),
                TestContext.Current.CancellationToken);
    }
}
