namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class TeamAccountRegistrationToken
{
    public Guid Id { get; set; }
    public Guid TeamAccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public TeamAccountRegistrationTokenPurpose Purpose { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal enum TeamAccountRegistrationTokenPurpose
{
    ContactEmailVerification,
    FirstUserSetup,
}
