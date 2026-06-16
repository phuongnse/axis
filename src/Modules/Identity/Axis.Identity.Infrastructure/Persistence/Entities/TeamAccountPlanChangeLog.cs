namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class TeamAccountPlanChangeLog
{
    public Guid Id { get; set; }
    public Guid TeamAccountId { get; set; }
    public Guid PreviousPlanId { get; set; }
    public Guid NewPlanId { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}
