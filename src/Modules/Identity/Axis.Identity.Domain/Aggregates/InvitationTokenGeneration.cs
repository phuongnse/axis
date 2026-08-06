using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class InvitationTokenGeneration : Entity<Guid>
{
    private InvitationTokenGeneration() : base(Guid.Empty)
    {
        TokenHash = string.Empty;
        DeliveryCorrelation = string.Empty;
    }

    private InvitationTokenGeneration(
        int generation,
        string tokenHash,
        string deliveryEnvelope,
        string deliveryCorrelation,
        DateTime expiresAt)
        : base(Guid.NewGuid())
    {
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        if (string.IsNullOrWhiteSpace(deliveryEnvelope))
            throw new ArgumentException("Delivery envelope is required.", nameof(deliveryEnvelope));
        if (string.IsNullOrWhiteSpace(deliveryCorrelation))
            throw new ArgumentException("Delivery correlation is required.", nameof(deliveryCorrelation));

        Generation = generation;
        TokenHash = tokenHash;
        DeliveryEnvelope = deliveryEnvelope;
        DeliveryCorrelation = deliveryCorrelation;
        ExpiresAt = expiresAt;
        Status = InvitationTokenStatus.Active;
        DeliveryStatus = InvitationDeliveryStatus.Pending;
    }

    public int Generation { get; private set; }
    public string TokenHash { get; private set; }
    public string? DeliveryEnvelope { get; private set; }
    public string DeliveryCorrelation { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public InvitationTokenStatus Status { get; private set; }
    public InvitationDeliveryStatus DeliveryStatus { get; private set; }
    public int DeliveryAttempts { get; private set; }
    public DateTime? NextDeliveryAttemptAt { get; private set; }
    public string? LastDeliveryErrorCode { get; private set; }

    internal static InvitationTokenGeneration Create(
        int generation,
        string tokenHash,
        string deliveryEnvelope,
        string deliveryCorrelation,
        DateTime expiresAt) =>
        new(generation, tokenHash, deliveryEnvelope, deliveryCorrelation, expiresAt);

    internal void MarkDeliveryAttempt(DateTime nextAttemptAt, string? errorCode)
    {
        DeliveryAttempts++;
        NextDeliveryAttemptAt = nextAttemptAt;
        LastDeliveryErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim();
    }

    internal void RecordDeliveryFailure(DateTime nextAttemptAt, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("Delivery error code is required.", nameof(errorCode));

        NextDeliveryAttemptAt = nextAttemptAt;
        LastDeliveryErrorCode = errorCode.Trim();
    }

    internal void MarkDelivered()
    {
        DeliveryStatus = InvitationDeliveryStatus.Delivered;
        DeliveryEnvelope = null;
        NextDeliveryAttemptAt = null;
        LastDeliveryErrorCode = null;
    }

    internal void MarkTerminalDeliveryFailure(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("Delivery error code is required.", nameof(errorCode));

        DeliveryStatus = InvitationDeliveryStatus.Failed;
        NextDeliveryAttemptAt = null;
        LastDeliveryErrorCode = errorCode.Trim();
    }

    internal void Exchange()
    {
        EnsureStatus(InvitationTokenStatus.Active);
        Status = InvitationTokenStatus.Exchanged;
        DeliveryEnvelope = null;
    }

    internal void Supersede()
    {
        EnsureMutable();
        Status = InvitationTokenStatus.Superseded;
        DeliveryEnvelope = null;
    }

    internal void Revoke()
    {
        EnsureMutable();
        Status = InvitationTokenStatus.Revoked;
        DeliveryEnvelope = null;
    }

    internal void Accept()
    {
        if (Status is not (InvitationTokenStatus.Active or InvitationTokenStatus.Exchanged))
            throw new InvalidOperationException("Only the current token generation can be accepted.");

        Status = InvitationTokenStatus.Accepted;
        DeliveryEnvelope = null;
    }

    internal void Expire()
    {
        EnsureMutable();
        Status = InvitationTokenStatus.Expired;
        DeliveryEnvelope = null;
    }

    private void EnsureMutable()
    {
        if (Status is not (InvitationTokenStatus.Active or InvitationTokenStatus.Exchanged))
            throw new InvalidOperationException("The token generation is already terminal.");
    }

    private void EnsureStatus(InvitationTokenStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException("The token generation is not active.");
    }
}
