namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class WorkspaceRegistrationToken
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public WorkspaceRegistrationTokenPurpose Purpose { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal enum WorkspaceRegistrationTokenPurpose
{
    ContactEmailVerification,
    FirstUserSetup,
}
