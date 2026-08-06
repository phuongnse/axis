using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class InvitationHandoff : Entity<Guid>
{
    private InvitationHandoff() : base(Guid.Empty)
    {
        HandoffHash = string.Empty;
    }

    private InvitationHandoff(int tokenGeneration, string handoffHash, DateTime expiresAt)
        : base(Guid.NewGuid())
    {
        if (tokenGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(tokenGeneration));
        if (string.IsNullOrWhiteSpace(handoffHash))
            throw new ArgumentException("Handoff hash is required.", nameof(handoffHash));

        TokenGeneration = tokenGeneration;
        HandoffHash = handoffHash;
        ExpiresAt = expiresAt;
        Status = InvitationHandoffStatus.Active;
    }

    public int TokenGeneration { get; private set; }
    public string HandoffHash { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public InvitationHandoffStatus Status { get; private set; }

    internal static InvitationHandoff Create(int tokenGeneration, string handoffHash, DateTime expiresAt) =>
        new(tokenGeneration, handoffHash, expiresAt);

    internal void Accept()
    {
        EnsureActive();
        Status = InvitationHandoffStatus.Accepted;
    }

    internal void Supersede()
    {
        EnsureActive();
        Status = InvitationHandoffStatus.Superseded;
    }

    internal void Revoke()
    {
        EnsureActive();
        Status = InvitationHandoffStatus.Revoked;
    }

    internal void Expire()
    {
        EnsureActive();
        Status = InvitationHandoffStatus.Expired;
    }

    private void EnsureActive()
    {
        if (Status != InvitationHandoffStatus.Active)
            throw new InvalidOperationException("The invitation handoff is already terminal.");
    }
}
