using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class WorkspaceMembership : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public Guid workspaceId { get; private set; }
    public WorkspaceMembershipStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private WorkspaceMembership(
        Guid id,
        Guid userId,
        Guid workspaceId,
        DateTime createdAt)
        : base(id)
    {
        UserId = userId;
        this.workspaceId = workspaceId;
        Status = WorkspaceMembershipStatus.Active;
        CreatedAt = createdAt;
    }

    public static WorkspaceMembership Create(Guid userId, Guid workspaceId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace is required.", nameof(workspaceId));

        return new WorkspaceMembership(Guid.NewGuid(), userId, workspaceId, DateTime.UtcNow);
    }

    public void Deactivate()
    {
        if (Status == WorkspaceMembershipStatus.Inactive)
            throw new InvalidOperationException("Membership is already inactive.");

        Status = WorkspaceMembershipStatus.Inactive;
    }

    public void Reactivate()
    {
        if (Status == WorkspaceMembershipStatus.Active)
            throw new InvalidOperationException("Membership is already active.");

        Status = WorkspaceMembershipStatus.Active;
    }
}
