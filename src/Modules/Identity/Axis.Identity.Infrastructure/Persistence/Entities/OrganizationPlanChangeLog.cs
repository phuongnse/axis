namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class OrganizationPlanChangeLog
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PreviousPlanId { get; set; }
    public Guid NewPlanId { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}
