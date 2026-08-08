using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Authorization.Application;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.Authorization.Infrastructure.Repositories;
using Axis.Authorization.Infrastructure.Services;
using Axis.Authorization.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Authorization.Infrastructure.Tests;

[Collection("AuthorizationDb")]
public sealed class AuthorizationAuditDispatchStoreTests(AuthorizationDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");

    [Fact]
    public async Task ClaimDueBatch_WhenStoresCompete_ClaimsEventOnce()
    {
        await ResetAsync();
        await PersistAsync(Guid.NewGuid());
        await using AuthorizationDbContext firstContext = database.CreateContext();
        await using AuthorizationDbContext secondContext = database.CreateContext();
        AuthorizationAuditDispatchStore first = new(firstContext);
        AuthorizationAuditDispatchStore second = new(secondContext);

        IReadOnlyList<AuthorizationAuditDispatchItem>[] claims = await Task.WhenAll(
            first.ClaimDueBatchAsync(Now, TimeSpan.FromSeconds(30), 1, TestContext.Current.CancellationToken),
            second.ClaimDueBatchAsync(Now, TimeSpan.FromSeconds(30), 1, TestContext.Current.CancellationToken));

        Assert.Single(claims.SelectMany(items => items));
    }

    [Fact]
    public async Task ClaimDueBatch_WhenLeaseExpires_ReclaimsAndIncrementsAttempt()
    {
        await ResetAsync();
        Guid eventId = Guid.NewGuid();
        await PersistAsync(eventId);
        AuthorizationAuditDispatchItem first;
        await using (AuthorizationDbContext context = database.CreateContext())
        {
            AuthorizationAuditDispatchStore store = new(context);
            first = Assert.Single(await store.ClaimDueBatchAsync(
                Now,
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken));
        }

        await using (AuthorizationDbContext context = database.CreateContext())
        {
            AuthorizationAuditDispatchStore store = new(context);
            Assert.Empty(await store.ClaimDueBatchAsync(
                Now.AddSeconds(29),
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken));
            AuthorizationAuditDispatchItem reclaimed = Assert.Single(await store.ClaimDueBatchAsync(
                Now.AddSeconds(30),
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken));
            Assert.Equal(eventId, reclaimed.Event.EventId);
            Assert.Equal(2, reclaimed.AttemptCount);
            Assert.NotEqual(first.LeaseId, reclaimed.LeaseId);
        }
    }

    [Fact]
    public async Task DeliveryStateTransitions_WhenLeaseChanges_RequireCurrentLeaseAndBoundFailureReason()
    {
        await ResetAsync();
        Guid eventId = Guid.NewGuid();
        await PersistAsync(eventId);
        DateTimeOffset retryAt = Now.AddMinutes(1);
        await using (AuthorizationDbContext context = database.CreateContext())
        {
            AuthorizationAuditDispatchStore store = new(context);
            AuthorizationAuditDispatchItem claim = Assert.Single(await store.ClaimDueBatchAsync(
                Now,
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken));
            Assert.False(await store.MarkDeliveredAsync(
                eventId,
                Guid.NewGuid(),
                TestContext.Current.CancellationToken));
            Assert.True(await store.MarkForRetryAsync(
                eventId,
                claim.LeaseId,
                retryAt,
                "transient",
                TestContext.Current.CancellationToken));
        }

        await using (AuthorizationDbContext context = database.CreateContext())
        {
            AuthorizationAuditDispatchStore store = new(context);
            AuthorizationAuditDispatchItem retry = Assert.Single(await store.ClaimDueBatchAsync(
                retryAt,
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken));
            Assert.True(await store.MarkPoisonedAsync(
                eventId,
                retry.LeaseId,
                new string('x', 300),
                TestContext.Current.CancellationToken));
        }

        await using AuthorizationDbContext read = database.CreateContext();
        AuthorizationAuditOutboxRow row = await read.AuditOutbox
            .AsNoTracking()
            .SingleAsync(value => value.Id == eventId, TestContext.Current.CancellationToken);
        Assert.Equal("Poisoned", row.DeliveryState);
        Assert.Null(row.NextAttemptAt);
        Assert.Null(row.LeaseId);
        Assert.Null(row.LeaseUntil);
        Assert.Equal(256, row.FailureReason?.Length);
        Assert.Equal(2, row.AttemptCount);
    }

    [Fact]
    public async Task ClaimDueBatch_WhenPayloadIsNullJson_PoisonsWithoutDispatching()
    {
        await ResetAsync();
        Guid eventId = Guid.NewGuid();
        await using (AuthorizationDbContext context = database.CreateContext())
        {
            await context.AuditOutbox.AddAsync(
                new AuthorizationAuditOutboxRow
                {
                    Id = eventId,
                    OccurredAt = Now,
                    Payload = "null",
                    NextAttemptAt = Now,
                    CreatedAt = Now,
                },
                TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (AuthorizationDbContext context = database.CreateContext())
        {
            AuthorizationAuditDispatchStore store = new(context);
            Assert.Empty(await store.ClaimDueBatchAsync(
                Now,
                TimeSpan.FromSeconds(30),
                1,
                TestContext.Current.CancellationToken));
        }

        await using AuthorizationDbContext read = database.CreateContext();
        AuthorizationAuditOutboxRow row = await read.AuditOutbox
            .AsNoTracking()
            .SingleAsync(value => value.Id == eventId, TestContext.Current.CancellationToken);
        Assert.Equal("Poisoned", row.DeliveryState);
        Assert.Equal("audit.payload_invalid", row.FailureReason);
    }

    [Fact]
    public async Task HealthReader_WhenRowsExist_ReturnsPoisonedCountAndOldestPendingTime()
    {
        await ResetAsync();
        await PersistAsync(Guid.NewGuid(), Now.AddMinutes(-2));
        await PersistAsync(Guid.NewGuid(), Now.AddMinutes(-1));
        await using AuthorizationDbContext context = database.CreateContext();
        AuthorizationAuditOutboxRow poisoned = new()
        {
            Id = Guid.NewGuid(),
            OccurredAt = Now,
            Payload = JsonSerializer.Serialize(Event(Guid.NewGuid())),
            DeliveryState = "Poisoned",
            FailureReason = "audit.delivery_rejected",
            CreatedAt = Now,
        };
        await context.AuditOutbox.AddAsync(poisoned, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        AuthorizationAuditHealthSnapshot snapshot = await new AuthorizationAuditHealthReader(context)
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, snapshot.PoisonedCount);
        Assert.Equal(Now.AddMinutes(-2), snapshot.OldestPendingAt);
    }

    private async Task PersistAsync(Guid eventId, DateTimeOffset? createdAt = null)
    {
        DateTimeOffset timestamp = createdAt ?? Now;
        await using AuthorizationDbContext context = database.CreateContext();
        await context.AuditOutbox.AddAsync(
            new AuthorizationAuditOutboxRow
            {
                Id = eventId,
                OccurredAt = timestamp,
                Payload = JsonSerializer.Serialize(Event(eventId, timestamp)),
                NextAttemptAt = timestamp,
                CreatedAt = timestamp,
            },
            TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ResetAsync()
    {
        await using AuthorizationDbContext context = database.CreateContext();
        await context.AuditOutbox.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private static AuditEventV1 Event(Guid eventId, DateTimeOffset? occurredAt = null) => new(
        eventId,
        AuditActorKindV1.Human,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "authorization.role.assigned",
        "product_role_assignment",
        Guid.NewGuid(),
        "succeeded",
        occurredAt ?? Now,
        $"corr-{eventId:N}",
        new Dictionary<string, string> { ["policy"] = "reference" });
}

public sealed class AuthorizationAuditDispatcherTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");

    [Fact]
    public async Task DispatchBatch_WhenCentralReadBackMatches_MarksDelivered()
    {
        AuditEventV1 auditEvent = Event();
        FakeDispatchStore store = new(auditEvent);
        FakeAuditSink sink = new(
            new AuditIngestionResult(AuditIngestionDisposition.Stored, null),
            ReadBack(auditEvent));

        Assert.Equal(1, await DispatchAsync(store, sink));

        Assert.True(store.Delivered);
        Assert.Null(store.RetryAt);
        Assert.Null(store.PoisonReason);
    }

    [Fact]
    public async Task DispatchBatch_WhenCentralDeliveryIsTransient_SchedulesBackoff()
    {
        AuditEventV1 auditEvent = Event();
        FakeDispatchStore store = new(auditEvent);
        FakeAuditSink sink = new(new InvalidOperationException("unavailable"));

        Assert.Equal(1, await DispatchAsync(store, sink));

        Assert.Equal(Now.AddSeconds(1), store.RetryAt);
        Assert.Equal("audit.delivery_transient", store.FailureReason);
        Assert.False(store.Delivered);
    }

    [Fact]
    public async Task DispatchBatch_WhenCentralRejects_PoisonsEvent()
    {
        AuditEventV1 auditEvent = Event();
        FakeDispatchStore store = new(auditEvent);
        FakeAuditSink sink = new(
            new AuditIngestionResult(
                AuditIngestionDisposition.Rejected,
                null,
                "audit.invalid"),
            null);

        Assert.Equal(1, await DispatchAsync(store, sink));

        Assert.Equal("audit.invalid", store.PoisonReason);
        Assert.Null(store.RetryAt);
        Assert.False(store.Delivered);
    }

    private static async Task<int> DispatchAsync(
        IAuthorizationAuditDispatchStore store,
        IAuditEventSink sink)
    {
        ServiceCollection services = new();
        services.AddSingleton(store);
        services.AddSingleton(sink);
        await using ServiceProvider provider = services.BuildServiceProvider();
        AuthorizationAuditDispatcher dispatcher = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now),
            NullLogger<AuthorizationAuditDispatcher>.Instance);
        return await dispatcher.DispatchBatchAsync(TestContext.Current.CancellationToken);
    }

    private static AuditEventV1 Event() => new(
        Guid.NewGuid(),
        AuditActorKindV1.Human,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "authorization.access.evaluated",
        "product_action",
        Guid.NewGuid(),
        "allowed",
        Now,
        $"corr-{Guid.NewGuid():N}");

    private static AuditEventReadBackV1 ReadBack(AuditEventV1 auditEvent) => new(
        auditEvent.EventId,
        auditEvent.ActorKind,
        auditEvent.ActorId,
        auditEvent.SubjectId,
        auditEvent.WorkspaceId,
        auditEvent.Action,
        auditEvent.TargetType,
        auditEvent.TargetId,
        auditEvent.Outcome,
        auditEvent.OccurredAt,
        auditEvent.CorrelationId,
        new Dictionary<string, string>());

    private sealed class FakeDispatchStore(AuditEventV1 auditEvent) : IAuthorizationAuditDispatchStore
    {
        private readonly Guid _leaseId = Guid.NewGuid();
        private bool _claimed;

        public bool Delivered { get; private set; }
        public DateTimeOffset? RetryAt { get; private set; }
        public string? FailureReason { get; private set; }
        public string? PoisonReason { get; private set; }

        public Task<IReadOnlyList<AuthorizationAuditDispatchItem>> ClaimDueBatchAsync(
            DateTimeOffset now,
            TimeSpan leaseLifetime,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AuthorizationAuditDispatchItem> items = _claimed
                ? []
                : [new AuthorizationAuditDispatchItem(auditEvent, 1, _leaseId)];
            _claimed = true;
            return Task.FromResult(items);
        }

        public Task<bool> MarkDeliveredAsync(
            Guid eventId,
            Guid leaseId,
            CancellationToken cancellationToken = default)
        {
            Delivered = eventId == auditEvent.EventId && leaseId == _leaseId;
            return Task.FromResult(Delivered);
        }

        public Task<bool> MarkForRetryAsync(
            Guid eventId,
            Guid leaseId,
            DateTimeOffset nextAttemptAt,
            string failureReason,
            CancellationToken cancellationToken = default)
        {
            RetryAt = nextAttemptAt;
            FailureReason = failureReason;
            return Task.FromResult(eventId == auditEvent.EventId && leaseId == _leaseId);
        }

        public Task<bool> MarkPoisonedAsync(
            Guid eventId,
            Guid leaseId,
            string failureReason,
            CancellationToken cancellationToken = default)
        {
            PoisonReason = failureReason;
            return Task.FromResult(eventId == auditEvent.EventId && leaseId == _leaseId);
        }
    }

    private sealed class FakeAuditSink : IAuditEventSink
    {
        private readonly AuditIngestionResult? _ingestion;
        private readonly AuditEventReadBackV1? _readBack;
        private readonly Exception? _exception;

        public FakeAuditSink(AuditIngestionResult ingestion, AuditEventReadBackV1? readBack)
        {
            _ingestion = ingestion;
            _readBack = readBack;
        }

        public FakeAuditSink(Exception exception) => _exception = exception;

        public Task<AuditIngestionResult> IngestAsync(
            AuditEventV1 auditEvent,
            CancellationToken cancellationToken = default) =>
            _exception is null
                ? Task.FromResult(_ingestion!)
                : Task.FromException<AuditIngestionResult>(_exception);

        public Task<AuditEventReadBackV1?> ReadBackAsync(
            Guid eventId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_readBack);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
