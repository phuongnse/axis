namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class TenantRegistrationToken
{
    public Guid Id { get; set; }
    public Guid tenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public TenantRegistrationTokenPurpose Purpose { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal enum TenantRegistrationTokenPurpose
{
    ContactEmailVerification,
    FirstUserSetup,
}
