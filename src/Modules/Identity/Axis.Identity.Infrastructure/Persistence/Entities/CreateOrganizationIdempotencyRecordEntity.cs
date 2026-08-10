namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class CreateOrganizationIdempotencyRecordEntity
{
    public string ScopedKey { get; set; } = null!;
    public string CanonicalRequest { get; set; } = null!;
    public Guid OrganizationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
