using System.Reflection;
using Axis.Mcp.Tools;

namespace Axis.Mcp.Tests;

public sealed class AxisMcpConfirmationStoreTests
{
    [Fact]
    public void ConfirmationStore_WhenTokenIsReplayed_RejectsSecondConsume()
    {
        AxisMcpConfirmationStore store = new();
        Guid definitionId = Guid.NewGuid();
        BusinessObjectPublishConfirmation confirmation = store.Create(
            definitionId,
            expectedRevision: 4,
            subject: "user:workspace",
            snapshotHash: "snapshot-hash");

        Assert.True(store.TryConsume(
            confirmation.Token,
            definitionId,
            expectedRevision: 4,
            subject: "user:workspace",
            snapshotHash: "snapshot-hash"));
        Assert.False(store.TryConsume(
            confirmation.Token,
            definitionId,
            expectedRevision: 4,
            subject: "user:workspace",
            snapshotHash: "snapshot-hash"));
    }

    [Fact]
    public void ConfirmationStore_WhenSnapshotChanges_RejectsAndPreservesToken()
    {
        AxisMcpConfirmationStore store = new();
        Guid definitionId = Guid.NewGuid();
        BusinessObjectPublishConfirmation confirmation = store.Create(
            definitionId,
            expectedRevision: 2,
            subject: "user:workspace",
            snapshotHash: "original");

        Assert.False(store.TryConsume(
            confirmation.Token,
            definitionId,
            expectedRevision: 2,
            subject: "user:workspace",
            snapshotHash: "changed"));
        Assert.True(store.TryConsume(
            confirmation.Token,
            definitionId,
            expectedRevision: 2,
            subject: "user:workspace",
            snapshotHash: "original"));
    }

    [Fact]
    public void ConfirmationStore_WhenTokenExpires_RejectsConsume()
    {
        MutableTimeProvider clock = new(DateTimeOffset.UtcNow);
        AxisMcpConfirmationStore store = new(clock, TimeSpan.FromMinutes(5));
        Guid definitionId = Guid.NewGuid();
        BusinessObjectPublishConfirmation confirmation = store.Create(
            definitionId,
            expectedRevision: 1,
            subject: "user:workspace",
            snapshotHash: "snapshot");

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(store.TryConsume(
            confirmation.Token,
            definitionId,
            expectedRevision: 1,
            subject: "user:workspace",
            snapshotHash: "snapshot"));
    }

    [Fact]
    public void ConfirmationStore_WhenAbandonedTokensExpire_PrunesOnNextCreate()
    {
        MutableTimeProvider clock = new(DateTimeOffset.UtcNow);
        AxisMcpConfirmationStore store = new(clock, TimeSpan.FromMinutes(5));

        store.Create(
            Guid.NewGuid(),
            expectedRevision: 1,
            subject: "user:workspace",
            snapshotHash: "abandoned");
        clock.Advance(TimeSpan.FromMinutes(5));

        store.Create(
            Guid.NewGuid(),
            expectedRevision: 1,
            subject: "user:workspace",
            snapshotHash: "current");

        FieldInfo confirmations = typeof(AxisMcpConfirmationStore).GetField(
            "_confirmations",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        PropertyInfo count = confirmations.FieldType.GetProperty("Count")!;
        Assert.Equal(1, count.GetValue(confirmations.GetValue(store)));
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
