using Axis.Audit.Domain;
using FluentAssertions;

namespace Axis.Audit.Domain.Tests;

public sealed class AuditRecordTests
{
    [Fact]
    public void TryCreate_WhenMetadataContainsSensitiveKey_RejectsTheEvent()
    {
        bool created = AuditRecord.TryCreate(
            Guid.NewGuid(), AuditActorKind.Human, Guid.NewGuid(), null, Guid.NewGuid(), "workspace.created", "workspace",
            Guid.NewGuid(), "succeeded", DateTimeOffset.UtcNow, "correlation-1",
            new Dictionary<string, string> { ["session_token"] = "redacted" }, out _, out string? rejectionCode);

        created.Should().BeFalse();
        rejectionCode.Should().Be("audit.metadata_invalid");
    }

    [Fact]
    public void TryCreate_WhenEventIsValid_CopiesBoundedMetadataIntoImmutableRecord()
    {
        Dictionary<string, string> metadata = new() { ["transition_state"] = "completed" };

        bool created = AuditRecord.TryCreate(
            Guid.NewGuid(), AuditActorKind.Human, Guid.NewGuid(), null, Guid.NewGuid(), "workspace.created", "workspace",
            Guid.NewGuid(), "succeeded", DateTimeOffset.UtcNow, "correlation-1", metadata, out AuditRecord? record, out _);
        metadata["transition_state"] = "tampered";

        created.Should().BeTrue();
        record!.Metadata["transition_state"].Should().Be("completed");
        record.Matches(record.ActorKind, record.ActorId, null, record.WorkspaceId, record.Action, record.TargetType,
            record.TargetId, record.Outcome, record.OccurredAt, record.CorrelationId,
            new Dictionary<string, string> { ["transition_state"] = "completed" }).Should().BeTrue();
    }

    [Fact]
    public void TryCreate_WhenTimestampHasSubMicrosecondTicks_NormalizesForPostgresRoundTrips()
    {
        DateTimeOffset occurredAt = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero).AddTicks(7);

        AuditRecord.TryCreate(
            Guid.NewGuid(), AuditActorKind.Human, Guid.NewGuid(), null, Guid.NewGuid(), "workspace.created", "workspace",
            Guid.NewGuid(), "succeeded", occurredAt, "correlation-1", null, out AuditRecord? record, out _)
            .Should().BeTrue();

        record!.OccurredAt.Ticks.Should().Be(occurredAt.Ticks - 7);
        record.Matches(record.ActorKind, record.ActorId, null, record.WorkspaceId, record.Action, record.TargetType,
            record.TargetId, record.Outcome, occurredAt, record.CorrelationId, null).Should().BeTrue();
    }

    [Theory]
    [InlineData(AuditActorKind.Human, true, true)]
    [InlineData(AuditActorKind.ServiceIdentity, true, true)]
    [InlineData(AuditActorKind.System, false, true)]
    [InlineData(AuditActorKind.Anonymous, false, true)]
    [InlineData(AuditActorKind.Human, false, false)]
    [InlineData(AuditActorKind.System, true, false)]
    public void TryCreate_ActorKindAndIdentifier_CombineFailClosed(
        AuditActorKind actorKind,
        bool hasActorId,
        bool expectedCreated)
    {
        bool created = AuditRecord.TryCreate(
            Guid.NewGuid(), actorKind, hasActorId ? Guid.NewGuid() : null, null, Guid.NewGuid(),
            "invitation.replayed", "invitation", Guid.NewGuid(), "denied", DateTimeOffset.UtcNow,
            "correlation-1", null, out _, out string? rejectionCode);

        created.Should().Be(expectedCreated);
        if (!expectedCreated)
            rejectionCode.Should().Be("audit.actor_invalid");
    }
}
