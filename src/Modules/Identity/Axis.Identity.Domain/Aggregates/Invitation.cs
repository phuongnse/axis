using Axis.Identity.Domain.Events;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Invitation : AggregateRoot<Guid>
{
    private const int ExpiryHours = 48;

    public Email Email { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public string Token { get; private set; }
    public InvitationStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    private Invitation(Guid id, Email email, Guid organizationId, Guid roleId,
        Guid invitedByUserId, string token, DateTime expiresAt, DateTime createdAt)
        : base(id)
    {
        Email = email;
        OrganizationId = organizationId;
        RoleId = roleId;
        InvitedByUserId = invitedByUserId;
        Token = token;
        Status = InvitationStatus.Pending;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    public static Invitation Create(Email email, Guid organizationId, Guid roleId, Guid invitedByUserId)
    {
        var token = GenerateToken();
        var now = DateTime.UtcNow;
        var invitation = new Invitation(Guid.NewGuid(), email, organizationId, roleId,
            invitedByUserId, token, now.AddHours(ExpiryHours), now);

        invitation.RaiseDomainEvent(new InvitationCreated(invitation.Id, organizationId, email.Value, token));
        return invitation;
    }

    /// <summary>Test helper — creates an already-expired invitation.</summary>
    internal static Invitation CreateExpired(Email email, Guid organizationId, Guid roleId, Guid invitedByUserId)
    {
        var token = GenerateToken();
        var past = DateTime.UtcNow.AddHours(-1);
        return new Invitation(Guid.NewGuid(), email, organizationId, roleId,
            invitedByUserId, token, past, past.AddHours(-ExpiryHours));
    }

    public void Accept()
    {
        if (Status == InvitationStatus.Accepted)
            throw new InvalidOperationException("This invitation has already been used.");

        if (IsExpired || Status == InvitationStatus.Expired)
            throw new InvalidOperationException("This invitation has expired.");

        if (Status == InvitationStatus.Cancelled)
            throw new InvalidOperationException("This invitation has been cancelled.");

        Status = InvitationStatus.Accepted;
        RaiseDomainEvent(new InvitationAccepted(Id, OrganizationId, Email.Value));
    }

    public void Cancel()
    {
        if (Status == InvitationStatus.Accepted)
            throw new InvalidOperationException("Cannot cancel an accepted invitation.");

        Status = InvitationStatus.Cancelled;
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").Replace("=", "");
}
