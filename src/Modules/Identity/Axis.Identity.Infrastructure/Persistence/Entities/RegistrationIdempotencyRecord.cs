namespace Axis.Identity.Infrastructure.Persistence.Entities;

/// <summary>US-001: deduplicates rapid registration submissions sharing the same Idempotency-Key.</summary>
internal sealed class RegistrationIdempotencyRecord
{
    public string IdempotencyKey { get; set; } = null!;
    public RegistrationIdempotencyStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
