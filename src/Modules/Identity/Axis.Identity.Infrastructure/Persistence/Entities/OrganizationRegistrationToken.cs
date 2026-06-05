namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class OrganizationRegistrationToken
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public OrganizationRegistrationTokenPurpose Purpose { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal enum OrganizationRegistrationTokenPurpose
{
    ContactEmailVerification,
    FirstUserSetup,
}
