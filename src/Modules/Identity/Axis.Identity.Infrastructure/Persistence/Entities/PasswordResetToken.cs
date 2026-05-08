namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsValid => UsedAt is null && DateTime.UtcNow < ExpiresAt;
}
