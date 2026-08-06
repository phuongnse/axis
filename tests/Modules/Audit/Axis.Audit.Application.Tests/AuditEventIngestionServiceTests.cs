using Axis.Audit.Application;
using Axis.Audit.Application.Persistence;
using Axis.Audit.Contracts;
using Axis.Audit.Domain;
using FluentAssertions;

namespace Axis.Audit.Application.Tests;

public sealed class AuditEventIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_WhenSameEventIsRetried_ReturnsPersistedReadBackWithoutAppendingAgain()
    {
        InMemoryAuditStore store = new();
        AuditEventIngestionService service = new(store, store);
        AuditEventV1 auditEvent = Event();

        AuditIngestionResult first = await service.IngestAsync(auditEvent, TestContext.Current.CancellationToken);
        AuditIngestionResult retry = await service.IngestAsync(auditEvent, TestContext.Current.CancellationToken);

        first.Disposition.Should().Be(AuditIngestionDisposition.Stored);
        retry.Disposition.Should().Be(AuditIngestionDisposition.AlreadyStored);
        retry.Event.Should().BeEquivalentTo(first.Event);
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task IngestAsync_WhenEventIdIsReusedWithDifferentPayload_FailsClosed()
    {
        InMemoryAuditStore store = new();
        AuditEventIngestionService service = new(store, store);
        AuditEventV1 auditEvent = Event();
        await service.IngestAsync(auditEvent, TestContext.Current.CancellationToken);

        AuditIngestionResult result = await service.IngestAsync(auditEvent with { Outcome = "denied" }, TestContext.Current.CancellationToken);

        result.Disposition.Should().Be(AuditIngestionDisposition.Conflict);
        result.RejectionCode.Should().Be("audit.event_id_conflict");
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task IngestAsync_WhenAnonymousEventHasActorId_RejectsAtContractBoundary()
    {
        InMemoryAuditStore store = new();
        AuditEventIngestionService service = new(store, store);

        AuditIngestionResult result = await service.IngestAsync(
            Event() with { ActorKind = AuditActorKindV1.Anonymous, ActorId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        result.Disposition.Should().Be(AuditIngestionDisposition.Rejected);
        result.RejectionCode.Should().Be("audit.actor_invalid");
        store.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(AuditActorKindV1.Human, true)]
    [InlineData(AuditActorKindV1.ServiceIdentity, true)]
    [InlineData(AuditActorKindV1.System, false)]
    [InlineData(AuditActorKindV1.Anonymous, false)]
    public async Task IngestAsync_WhenActorCombinationIsValid_StoresTheVersionedEnvelope(
        AuditActorKindV1 actorKind,
        bool hasActorId)
    {
        InMemoryAuditStore store = new();
        AuditEventIngestionService service = new(store, store);

        AuditIngestionResult result = await service.IngestAsync(
            Event() with { ActorKind = actorKind, ActorId = hasActorId ? Guid.NewGuid() : null },
            TestContext.Current.CancellationToken);

        result.Disposition.Should().Be(AuditIngestionDisposition.Stored);
        result.Event!.ActorKind.Should().Be(actorKind);
        if (hasActorId)
            result.Event.ActorId.Should().NotBeNull();
        else
            result.Event.ActorId.Should().BeNull();
    }

    private static AuditEventV1 Event() => new(
        Guid.NewGuid(), AuditActorKindV1.Human, Guid.NewGuid(), null, Guid.NewGuid(), "workspace.created", "workspace",
        Guid.NewGuid(), "succeeded", DateTimeOffset.UtcNow, "correlation-1",
        new Dictionary<string, string> { ["transition_state"] = "completed" });

    private sealed class InMemoryAuditStore : IAuditRecordRepository, IAuditUnitOfWork
    {
        private readonly Dictionary<Guid, AuditRecord> _records = [];
        private AuditRecord? _pending;
        public int Count => _records.Count;

        public Task AddAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            _pending = record;
            return Task.CompletedTask;
        }

        public Task<AuditRecord?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.GetValueOrDefault(eventId));

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_pending is not null)
            {
                _records.Add(_pending.EventId, _pending);
                _pending = null;
            }
            return Task.CompletedTask;
        }
    }
}
