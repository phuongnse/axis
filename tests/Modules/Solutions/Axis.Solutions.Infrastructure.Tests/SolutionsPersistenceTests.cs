using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Persistence.Entities;
using Axis.Solutions.Infrastructure.Extensions;
using Axis.Solutions.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Axis.Audit.Contracts;
using Axis.Solutions.Infrastructure.Services;

namespace Axis.Solutions.Infrastructure.Tests;

[Collection("SolutionsDb")]
public sealed class SolutionsPersistenceTests(SolutionsDatabaseFixture db)
{
    [Fact]
    public async Task Persistence_AtomicSave_RetainsComponentsAndAudit()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionVersion version = Version(now);
        await using (SolutionsDbContext context = db.CreateContext())
        {
            await context.SolutionVersions.AddAsync(version, TestContext.Current.CancellationToken);
            await context.Components.AddAsync(new SolutionComponentRecord
            {
                SolutionVersionId = version.Id, Type = "authorization.policy.v1", Key = "reference", Sha256 = new string('c', 64), Content = [1], DependsOnJson = "[]",
            }, TestContext.Current.CancellationToken);
            Guid auditEventId = Guid.NewGuid();
            await context.AuditOutbox.AddAsync(new SolutionsAuditOutboxRecord { EventId = auditEventId, ActorKind = AuditActorKindV1.System, CorrelationId = $"solutions-{auditEventId:N}", EventType = "solutions.version.published", SolutionVersionId = version.Id, Outcome = "succeeded", OccurredAt = now, CreatedAt = now, NextAttemptAt = now }, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using SolutionsDbContext read = db.CreateContext();
        Assert.Equal(1, await read.SolutionVersions.CountAsync(x => x.Id == version.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, await read.Components.CountAsync(x => x.SolutionVersionId == version.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, await read.AuditOutbox.CountAsync(x => x.SolutionVersionId == version.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Persistence_DuplicateIdempotency_Rejects()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionVersion version = Version(now);
        SolutionInstallation installation = SolutionInstallation.Create(Guid.NewGuid(), version.Id, now);
        SolutionInstallationOperation operation = SolutionInstallationOperation.Create(installation.WorkspaceId, Guid.NewGuid(), SolutionSubjectKind.Human, "test-correlation", installation.Id, "request-1", new string('d', 64), [new("authorization.policy.v1", "reference", new string('e', 64), [])], now);
        await using (SolutionsDbContext context = db.CreateContext())
        {
            await context.SolutionVersions.AddAsync(version, TestContext.Current.CancellationToken);
            await context.SolutionInstallations.AddAsync(installation, TestContext.Current.CancellationToken);
            await context.SolutionOperations.AddAsync(operation, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using SolutionsDbContext duplicate = db.CreateContext();
        await duplicate.SolutionOperations.AddAsync(SolutionInstallationOperation.Create(installation.WorkspaceId, Guid.NewGuid(), SolutionSubjectKind.Human, "test-correlation", installation.Id, "request-1", new string('f', 64), [new("authorization.policy.v1", "another", new string('e', 64), [])], now), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Persistence_ExpiredApplyingLease_ReclaimsAndFencesAcrossContexts()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
        SolutionVersion version = Version(now);
        SolutionInstallation installation = SolutionInstallation.Create(
            Guid.NewGuid(),
            version.Id,
            now);
        SolutionInstallationOperation operation = SolutionInstallationOperation.Create(
            installation.WorkspaceId,
            Guid.NewGuid(),
            SolutionSubjectKind.Human,
            "test-correlation",
            installation.Id,
            $"lease-{Guid.NewGuid():N}",
            new string('d', 64),
            [new("authorization.policy.v1", "reference", new string('e', 64), [])],
            now);
        long firstEpoch = operation.AcquireLease(now, TimeSpan.FromMinutes(1));
        Guid stepId = operation.ClaimNext(firstEpoch, now.AddSeconds(1)).Id;

        await using (SolutionsDbContext context = db.CreateContext())
        {
            await context.SolutionVersions.AddAsync(version, TestContext.Current.CancellationToken);
            await context.SolutionInstallations.AddAsync(installation, TestContext.Current.CancellationToken);
            await context.SolutionOperations.AddAsync(operation, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (SolutionsDbContext reclaim = db.CreateContext())
        {
            SolutionInstallationOperation persisted = await reclaim.SolutionOperations
                .Include(value => value.Steps)
                .SingleAsync(
                    value => value.Id == operation.Id,
                    TestContext.Current.CancellationToken);
            Assert.Equal(InstallationStepStatus.Applying, Assert.Single(persisted.Steps).Status);
            long secondEpoch = persisted.AcquireLease(
                now.AddMinutes(2),
                TimeSpan.FromMinutes(1));
            Assert.Equal(firstEpoch + 1, secondEpoch);
            Assert.Throws<InvalidOperationException>(() =>
                persisted.Confirm(stepId, firstEpoch, now.AddMinutes(2).AddSeconds(1)));
            await reclaim.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using SolutionsDbContext read = db.CreateContext();
        SolutionInstallationOperation reclaimed = await read.SolutionOperations
            .Include(value => value.Steps)
            .SingleAsync(
                value => value.Id == operation.Id,
                TestContext.Current.CancellationToken);
        Assert.Equal(InstallationOperationStatus.Running, reclaimed.Status);
        Assert.Equal(firstEpoch + 1, reclaimed.LeaseEpoch);
        SolutionInstallationStep step = Assert.Single(reclaimed.Steps);
        Assert.Equal(InstallationStepStatus.Pending, step.Status);
        Assert.Equal(firstEpoch, step.ReclaimedEpoch);
    }

    [Fact]
    public async Task Ledger_RevokedKey_RefusesResurrection()
    {
        await ResetPublisherLedgerAsync();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ServiceProvider provider = CreateProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITrustedPublisherLedger ledger = scope.ServiceProvider.GetRequiredService<ITrustedPublisherLedger>();
            await ledger.ReconcileAsync(1, [new("axis", "release_key", key.ExportSubjectPublicKeyInfoPem(), true)], TestContext.Current.CancellationToken);
            await ledger.ReconcileAsync(2, [], TestContext.Current.CancellationToken);
        }
        await using (SolutionsDbContext read = db.CreateContext())
        {
            TrustedPublisherKey stored = await read.TrustedPublisherKeys.SingleAsync(TestContext.Current.CancellationToken);
            Assert.True(stored.IsTombstone);
            Assert.Equal(TrustedPublisherKeyStatus.Revoked, stored.Status);
        }
        await using AsyncServiceScope retryScope = provider.CreateAsyncScope();
        ITrustedPublisherLedger retry = retryScope.ServiceProvider.GetRequiredService<ITrustedPublisherLedger>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => retry.ReconcileAsync(3, [new("axis", "release_key", key.ExportSubjectPublicKeyInfoPem(), true)], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ledger_SameRevisionRetry_RequiresCanonicalSnapshot()
    {
        await ResetPublisherLedgerAsync();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa substitute = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string keyId = $"same_revision_{Guid.NewGuid():N}";
        TrustedPublisherConfigurationKey configured = new("axis", keyId, key.ExportSubjectPublicKeyInfoPem(), true);
        using ServiceProvider provider = CreateProvider();
        await using (AsyncServiceScope firstScope = provider.CreateAsyncScope())
            await firstScope.ServiceProvider.GetRequiredService<ITrustedPublisherLedger>()
                .ReconcileAsync(10, [configured], TestContext.Current.CancellationToken);

        await using (AsyncServiceScope retryScope = provider.CreateAsyncScope())
        {
            ITrustedPublisherLedger retry = retryScope.ServiceProvider.GetRequiredService<ITrustedPublisherLedger>();
            Assert.Empty(await retry.ReconcileAsync(10, [configured], TestContext.Current.CancellationToken));
        }

        await using AsyncServiceScope conflictScope = provider.CreateAsyncScope();
        ITrustedPublisherLedger conflict = conflictScope.ServiceProvider.GetRequiredService<ITrustedPublisherLedger>();
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => conflict.ReconcileAsync(
            10,
            [new("axis", keyId, substitute.ExportSubjectPublicKeyInfoPem(), true)],
            TestContext.Current.CancellationToken));
        Assert.Equal("solutions.publisher_configuration.revision_conflict", exception.Message);
    }

    private async Task ResetPublisherLedgerAsync()
    {
        await using SolutionsDbContext context = db.CreateContext();
        await context.TrustedPublisherKeys.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await context.TrustedPublisherLedgerState.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AuditDispatcher_PendingRecord_DeliversRedactedReadback()
    {
        Guid eventId = Guid.NewGuid();
        await using (SolutionsDbContext context = db.CreateContext())
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await context.AuditOutbox.AddAsync(new SolutionsAuditOutboxRecord { EventId = eventId, ActorKind = AuditActorKindV1.System, SubjectId = Guid.NewGuid(), CorrelationId = "origin-correlation", OriginatingSubjectKind = SolutionSubjectKind.Human, EventType = "solutions.install.step", WorkspaceId = Guid.NewGuid(), OperationId = Guid.NewGuid(), Outcome = "succeeded", ProblemCode = "safe_code", OccurredAt = now, CreatedAt = now, NextAttemptAt = now }, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        CapturingAuditSink sink = new();
        using ServiceProvider provider = CreateProvider(sink);
        SolutionsAuditDispatchWorker worker = provider.GetRequiredService<SolutionsAuditDispatchWorker>();
        SolutionsAuditOutboxHealth health = await worker.DispatchAsync(10, TestContext.Current.CancellationToken);
        Assert.True(health.Delivered >= 1);
        AuditEventV1 delivered = Assert.Single(sink.Events, value => value.EventId == eventId);
        Assert.Equal(eventId, delivered.EventId);
        Assert.Equal(AuditActorKindV1.System, delivered.ActorKind);
        Assert.Null(delivered.ActorId);
        Assert.NotNull(delivered.SubjectId);
        Assert.Equal("origin-correlation", delivered.CorrelationId);
        Assert.Equal("human", delivered.Metadata?["originating_subject_kind"]);
        Assert.Equal("solution_operation", delivered.TargetType);
        await using SolutionsDbContext read = db.CreateContext();
        Assert.Equal("Delivered", (await read.AuditOutbox.SingleAsync(x => x.EventId == eventId, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task AuditDispatcher_MissingCanonicalReadback_SchedulesRetryAndClearsLease()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        Guid eventId = await SeedAuditAsync(now);
        CapturingAuditSink sink = new(readBackAvailable: false);
        using ServiceProvider provider = CreateProvider(sink, new FixedClock(now));

        await provider.GetRequiredService<SolutionsAuditDispatchWorker>()
            .DispatchAsync(10, TestContext.Current.CancellationToken);

        await using SolutionsDbContext read = db.CreateContext();
        SolutionsAuditOutboxRecord row = await read.AuditOutbox.SingleAsync(
            value => value.EventId == eventId,
            TestContext.Current.CancellationToken);
        Assert.Equal("Retrying", row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(now.AddSeconds(1), row.NextAttemptAt);
        Assert.Null(row.LeaseId);
        Assert.Null(row.LeaseUntil);
        Assert.Equal("audit.readback_unconfirmed", row.LastError);
        read.AuditOutbox.Remove(row);
        await read.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AuditDispatcher_InvalidEnvelope_PoisonsBeforeCentralIngest()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        Guid eventId = await SeedAuditAsync(now, row =>
        {
            row.ActorKind = AuditActorKindV1.Human;
            row.ActorId = null;
            row.SubjectId = null;
        });
        CapturingAuditSink sink = new();
        using ServiceProvider provider = CreateProvider(sink, new FixedClock(now));

        SolutionsAuditOutboxHealth health = await provider
            .GetRequiredService<SolutionsAuditDispatchWorker>()
            .DispatchAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(0, sink.IngestCalls);
        Assert.True(health.Poisoned >= 1);
        await using SolutionsDbContext read = db.CreateContext();
        SolutionsAuditOutboxRecord row = await read.AuditOutbox.SingleAsync(
            value => value.EventId == eventId,
            TestContext.Current.CancellationToken);
        Assert.Equal("Poisoned", row.Status);
        Assert.Equal("audit.actor_invalid", row.LastError);
        Assert.Null(row.LeaseId);
    }

    [Fact]
    public async Task AuditDispatcher_CentralRejection_PoisonsWithBoundedReason()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        Guid eventId = await SeedAuditAsync(now);
        CapturingAuditSink sink = new(
            disposition: AuditIngestionDisposition.Rejected,
            rejectionCode: new string('x', 300));
        using ServiceProvider provider = CreateProvider(sink, new FixedClock(now));

        await provider.GetRequiredService<SolutionsAuditDispatchWorker>()
            .DispatchAsync(10, TestContext.Current.CancellationToken);

        await using SolutionsDbContext read = db.CreateContext();
        SolutionsAuditOutboxRecord row = await read.AuditOutbox.SingleAsync(
            value => value.EventId == eventId,
            TestContext.Current.CancellationToken);
        Assert.Equal("Poisoned", row.Status);
        Assert.Equal(200, row.LastError?.Length);
        Assert.Null(row.NextAttemptAt);
        Assert.Null(row.LeaseId);
    }

    [Fact]
    public async Task AuditDispatcher_ExpiredDeliveryLease_IsReclaimed()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        Guid eventId = await SeedAuditAsync(now, row =>
        {
            row.Status = "Delivering";
            row.AttemptCount = 3;
            row.LeaseId = Guid.NewGuid();
            row.LeaseUntil = now.AddSeconds(-1);
        });
        CapturingAuditSink sink = new();
        using ServiceProvider provider = CreateProvider(sink, new FixedClock(now));

        await provider.GetRequiredService<SolutionsAuditDispatchWorker>()
            .DispatchAsync(10, TestContext.Current.CancellationToken);

        await using SolutionsDbContext read = db.CreateContext();
        SolutionsAuditOutboxRecord row = await read.AuditOutbox.SingleAsync(
            value => value.EventId == eventId,
            TestContext.Current.CancellationToken);
        Assert.Equal("Delivered", row.Status);
        Assert.Equal(4, row.AttemptCount);
        Assert.Null(row.LeaseId);
        Assert.Null(row.LeaseUntil);
    }

    [Fact]
    public async Task AuditDispatcher_ConcurrentWorkers_ClaimEventOnce()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        Guid eventId = await SeedAuditAsync(now);
        CapturingAuditSink sink = new();
        using ServiceProvider provider = CreateProvider(sink, new FixedClock(now));
        SolutionsAuditDispatchWorker worker = provider.GetRequiredService<SolutionsAuditDispatchWorker>();

        await Task.WhenAll(
            worker.DispatchAsync(1, TestContext.Current.CancellationToken),
            worker.DispatchAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(1, sink.IngestCalls);
        await using SolutionsDbContext read = db.CreateContext();
        SolutionsAuditOutboxRecord row = await read.AuditOutbox.SingleAsync(
            value => value.EventId == eventId,
            TestContext.Current.CancellationToken);
        Assert.Equal("Delivered", row.Status);
        Assert.Equal(1, row.AttemptCount);
    }

    private async Task<Guid> SeedAuditAsync(
        DateTimeOffset now,
        Action<SolutionsAuditOutboxRecord>? configure = null)
    {
        Guid eventId = Guid.NewGuid();
        SolutionsAuditOutboxRecord row = new()
        {
            EventId = eventId,
            ActorKind = AuditActorKindV1.System,
            CorrelationId = $"solutions-{eventId:N}",
            EventType = "solutions.install.step",
            WorkspaceId = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            Outcome = "succeeded",
            OccurredAt = now,
            CreatedAt = now,
            NextAttemptAt = now,
        };
        configure?.Invoke(row);
        await using SolutionsDbContext context = db.CreateContext();
        await context.AuditOutbox.AddAsync(row, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return eventId;
    }

    private ServiceProvider CreateProvider(
        IAuditEventSink? auditSink = null,
        TimeProvider? clock = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Solutions"] = db.ConnectionString }).Build();
        ServiceCollection services = new();
        if (clock is not null) services.AddSingleton(clock);
        services.AddSolutionsInfrastructure(configuration);
        if (auditSink is not null) services.AddSingleton(auditSink);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingAuditSink(
        bool readBackAvailable = true,
        AuditIngestionDisposition disposition = AuditIngestionDisposition.Stored,
        string? rejectionCode = null) : IAuditEventSink
    {
        private readonly ConcurrentDictionary<Guid, AuditEventV1> _events = [];
        public IEnumerable<AuditEventV1> Events => _events.Values;
        public int IngestCalls;
        public Task<AuditIngestionResult> IngestAsync(AuditEventV1 auditEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref IngestCalls);
            _events[auditEvent.EventId] = auditEvent;
            return Task.FromResult(new AuditIngestionResult(disposition, null, rejectionCode));
        }
        public Task<AuditEventReadBackV1?> ReadBackAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(!readBackAvailable || !_events.TryGetValue(eventId, out AuditEventV1? auditEvent)
                ? null
                : new AuditEventReadBackV1(
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
                    auditEvent.Metadata ?? new Dictionary<string, string>()));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SolutionVersion Version(DateTimeOffset now) => SolutionVersion.Create("reference_application", $"0.1.{Guid.NewGuid():N}"[..7], new string('a', 64), [1], new string('b', 64), "axis", "release_key", new string('c', 40), "build", now, new Uri("https://example.test/reference"), now);
}
