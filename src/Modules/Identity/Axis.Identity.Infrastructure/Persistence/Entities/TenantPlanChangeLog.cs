namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class TenantPlanChangeLog
{
    public Guid Id { get; set; }
    public Guid tenantId { get; set; }
    public Guid PreviousPlanId { get; set; }
    public Guid NewPlanId { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}
